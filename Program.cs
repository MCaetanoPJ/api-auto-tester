using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Bogus;

namespace SwaggerClientDemo
{
    class Program
    {
        public static async Task Main(string[] args)
        {
            var httpRequestManager = new HttpRequestManager();
            HttpRequestResult loginResult = null;

            // Solicita a URL do Swagger JSON
            Console.Write("Informe a URL do Swagger JSON: ");
            string swaggerUrl = Console.ReadLine();

            // Obtém a URL base (ex: https://api.exemplo.com)
            string baseUrl = GetBaseUrl(swaggerUrl);
            if (string.IsNullOrEmpty(baseUrl))
            {
                FileHelper.WriteToFile("URL base inválida.");
                return;
            }

            // 1. Obtém todos os endpoints definidos no Swagger
            var swaggerParser = new SwaggerParser();
            var endpoints = await swaggerParser.GetEndpointsAsync(swaggerUrl);

            if (!endpoints.Any())
            {
                FileHelper.WriteToFile("Nenhum endpoint foi encontrado na URL do Swagger informada.");
                return;
            }

            var precisaAutenticar = endpoints.Any(ep => ep.RequiresAuthentication);
            if (true)
            {
                // 2. Filtra os endpoints que foram identificados como de login
                var loginEndpoints = endpoints.Where(ep => ep.IsLoginEndpoint).ToList();
                if (!loginEndpoints.Any())
                {
                    FileHelper.WriteToFile("Nenhum endpoint de login foi identificado no Swagger.");
                    return;
                }

                FileHelper.WriteToFile("\nEndpoints identificados como login:");
                for (int i = 0; i < loginEndpoints.Count; i++)
                {
                    FileHelper.WriteToFile($"{i + 1}. {loginEndpoints[i].HttpMethod} {loginEndpoints[i].Path}");
                }
                Console.Write("Informe o número do endpoint de login que deseja usar: ");
                string loginSelectionInput = Console.ReadLine();
                if (!int.TryParse(loginSelectionInput, out int selectedIndex) ||
                    selectedIndex < 1 || selectedIndex > loginEndpoints.Count)
                {
                    FileHelper.WriteToFile("Seleção inválida. Encerrando.");
                    return;
                }
                var loginEndpoint = loginEndpoints[selectedIndex - 1];

                // 3. Se o endpoint possui parâmetros definidos no Swagger, permite que o usuário os atualize;
                //    caso contrário, solicita que o usuário informe um JSON de login.
                if (loginEndpoint.RequestBody != null && loginEndpoint.RequestBody.Any())
                {
                    FileHelper.WriteToFile("\nParâmetros do endpoint de login (valores fake já gerados):");
                    foreach (var kvp in loginEndpoint.RequestBody)
                    {
                        FileHelper.WriteToFile($" - {kvp.Key}: {kvp.Value}");
                    }
                    FileHelper.WriteToFile("Informe os valores para os parâmetros. Para manter o valor atual, pressione Enter:");
                    var updatedParameters = new Dictionary<string, object>();
                    foreach (var kvp in loginEndpoint.RequestBody)
                    {
                        Console.Write($"Valor para '{kvp.Key}' (default: {kvp.Value}): ");
                        string input = Console.ReadLine();
                        updatedParameters[kvp.Key] = string.IsNullOrWhiteSpace(input) ? kvp.Value : input;
                    }
                    loginEndpoint.RequestBody = updatedParameters;
                }
                else
                {
                    FileHelper.WriteToFile("\nO endpoint de login selecionado não possui parâmetros definidos no Swagger JSON.");
                    FileHelper.WriteToFile("Por favor, informe um JSON preenchido com os dados de login a ser usado:");
                    loginEndpoint.RequestBody = await ReadAndValidateJsonAsync();
                }

                // 4. Exibe o JSON de entrada que será usado para o login
                FileHelper.WriteToFile("\nJSON de entrada para o endpoint de login:");
                if (loginEndpoint.RequestBody != null && loginEndpoint.RequestBody.Any())
                {
                    string jsonUsed = JsonSerializer.Serialize(loginEndpoint.RequestBody, new JsonSerializerOptions { WriteIndented = true });
                    FileHelper.WriteToFile(jsonUsed);
                }
                else
                {
                    FileHelper.WriteToFile($"Endpoint: {loginEndpoint.HttpMethod} {loginEndpoint.Path} (sem corpo de requisição)");
                }

                // 5. Realiza a requisição de login em um loop até que ela seja bem-sucedida.
                do
                {
                    FileHelper.WriteToFile("\nRealizando requisição de login...");
                    loginResult = await httpRequestManager.SendRequestAsync(baseUrl, loginEndpoint, token: null);

                    FileHelper.WriteToFile("\nResultado da requisição de login:");
                    FileHelper.WriteToFile(loginResult.RequestInfo);
                    FileHelper.WriteToFile(loginResult.ResponseInfo);

                    if (!loginResult.IsSuccessStatusCode)
                    {
                        FileHelper.WriteToFile($"Falha no login. Status Code: {loginResult.StatusCode} - {loginResult.ReasonPhrase}");
                        FileHelper.WriteToFile("JSON enviado:");
                        if (loginEndpoint.RequestBody != null && loginEndpoint.RequestBody.Any())
                        {
                            string jsonUsed = JsonSerializer.Serialize(loginEndpoint.RequestBody, new JsonSerializerOptions { WriteIndented = true });
                            FileHelper.WriteToFile(jsonUsed);
                        }
                        FileHelper.WriteToFile("Por favor, informe outro JSON para os dados de login:");
                        loginEndpoint.RequestBody = await ReadAndValidateJsonAsync();
                    }
                } while (!loginResult.IsSuccessStatusCode);

                // Se o login for bem-sucedido, tenta extrair e armazenar o token da resposta
                FileHelper.WriteToFile("Login realizado com sucesso.");
                string tokenExtracted = TokenManager.ExtractToken(loginResult.JsonResponse);
                if (!string.IsNullOrEmpty(tokenExtracted))
                {
                    TokenManager.JwtToken = tokenExtracted;
                    FileHelper.WriteToFile("Token armazenado com sucesso.");
                }
                else
                {
                    FileHelper.WriteToFile("Login bem-sucedido, mas não foi possível extrair o token.");
                }
            }

            //Pergunta o tempo sem segundos entre uma requisição e outra
            FileHelper.WriteToFile("Informe em segundos, quanto tempo devemos aguardar entre uma requisição e outra?");
            var delayInSecondsResponse = Console.ReadLine();
            if (!int.TryParse(delayInSecondsResponse, out int delayInSeconds)) return;

            // 6. Executa as requisições dos demais endpoints, utilizando o token (caso disponível)
            FileHelper.WriteToFile("\nExecutando requisições dos demais endpoints...");
            foreach (var endpoint in endpoints)
            {
                // Pula o endpoint de login (já usado)
                if (endpoint.IsLoginEndpoint)
                    continue;

                FileHelper.WriteToFile($"\nRequisição: {endpoint.HttpMethod} {endpoint.Path}");
                if (endpoint.RequestBody != null && endpoint.RequestBody.Any())
                {
                    string jsonUsed = JsonSerializer.Serialize(endpoint.RequestBody, new JsonSerializerOptions { WriteIndented = true });
                    FileHelper.WriteToFile("JSON de entrada:");
                    FileHelper.WriteToFile(jsonUsed);
                }
                else
                {
                    FileHelper.WriteToFile($"Endpoint: {endpoint.HttpMethod} {endpoint.Path} (sem corpo de requisição)");
                }

                HttpRequestResult result = await httpRequestManager.SendRequestAsync(baseUrl, endpoint, TokenManager.JwtToken);
                FileHelper.WriteToFile("Resultado:");
                FileHelper.WriteToFile(result.RequestInfo);
                FileHelper.WriteToFile(result.ResponseInfo);

                // Aguardar X segundos antes da próxima requisição
                await Task.Delay(delayInSeconds * 1000);
            }
        }

        /// <summary>
        /// Extrai a URL base a partir da URL completa do Swagger.
        /// </summary>
        private static string GetBaseUrl(string swaggerUrl)
        {
            try
            {
                var uri = new Uri(swaggerUrl);
                return $"{uri.Scheme}://{uri.Host}{(uri.IsDefaultPort ? "" : $":{uri.Port}")}";
            }
            catch (Exception ex)
            {
                FileHelper.WriteToFile("Erro ao obter a URL base: " + ex.Message);
                return string.Empty;
            }
        }

        /// <summary>
        /// Lê JSON de login do console de forma multiline até que o usuário digite uma linha vazia e valida se o JSON é válido e não está vazio.
        /// </summary>
        private static async Task<Dictionary<string, object>> ReadAndValidateJsonAsync()
        {
            Dictionary<string, object> loginData = null;
            while (loginData == null || !loginData.Any())
            {
                FileHelper.WriteToFile("Cole o JSON de login. Digite uma linha vazia para terminar:");
                string jsonInput = ReadMultilineInput();
                try
                {
                    loginData = JsonSerializer.Deserialize<Dictionary<string, object>>(jsonInput);
                    if (loginData == null || !loginData.Any())
                    {
                        FileHelper.WriteToFile("JSON fornecido está vazio ou inválido. Tente novamente.");
                    }
                }
                catch (Exception ex)
                {
                    FileHelper.WriteToFile("Erro ao interpretar o JSON fornecido: " + ex.Message);
                    FileHelper.WriteToFile("Tente novamente.");
                    loginData = null;
                }
            }
            return loginData;
        }

        /// <summary>
        /// Lê input multiline do console até que o usuário informe uma linha vazia.
        /// </summary>
        private static string ReadMultilineInput()
        {
            StringBuilder sb = new StringBuilder();
            string line;
            while (!string.IsNullOrEmpty(line = Console.ReadLine()))
            {
                sb.AppendLine(line);
            }
            return sb.ToString();
        }
    }

    /// <summary>
    /// Responsável por fazer o parser do JSON do Swagger e retornar os endpoints.
    /// </summary>
    public class SwaggerParser
    {
        public async Task<List<SwaggerEndpoint>> GetEndpointsAsync(string swaggerUrl)
        {
            var endpoints = new List<SwaggerEndpoint>();

            try
            {
                using var client = new HttpClient();
                string swaggerJson = await client.GetStringAsync(swaggerUrl);
                using var swaggerDoc = JsonDocument.Parse(swaggerJson);

                if (swaggerDoc.RootElement.TryGetProperty("paths", out JsonElement paths))
                {
                    foreach (var pathElement in paths.EnumerateObject())
                    {
                        string path = pathElement.Name;
                        // Cada path pode definir vários métodos HTTP
                        foreach (var methodElement in pathElement.Value.EnumerateObject())
                        {
                            var endpoint = new SwaggerEndpoint
                            {
                                Path = path,
                                HttpMethod = methodElement.Name.ToUpper(),
                                RequestBody = null,
                                RequiresAuthentication = false,
                                IsLoginEndpoint = false
                            };

                            // Verifica a existência de segurança (ex.: array "security")
                            if (methodElement.Value.TryGetProperty("security", out JsonElement securityElement) &&
                                securityElement.ValueKind == JsonValueKind.Array &&
                                securityElement.GetArrayLength() > 0)
                            {
                                endpoint.RequiresAuthentication = true;
                            }

                            // Extrai parâmetros do requestBody e gera valores fake conforme o tipo
                            if (methodElement.Value.TryGetProperty("requestBody", out JsonElement requestBodyElement))
                            {
                                var parameters = ParseRequestBodyParameters(requestBodyElement, swaggerDoc);
                                if (parameters.Any())
                                {
                                    endpoint.RequestBody = parameters;
                                }
                            }

                            // Extrai parâmetros do parameters e gera valores fake conforme o tipo
                            if (methodElement.Value.TryGetProperty("parameters", out JsonElement parametersElement))
                            {
                                var parameters = ParseQueryParameters(parametersElement);
                                if (parameters.Any())
                                {
                                    endpoint.Parameter = parameters;
                                }
                            }

                            // Verifica as respostas para identificar se o endpoint retorna um token (ex.: "access_token" ou "token")
                            if (methodElement.Value.TryGetProperty("responses", out JsonElement responses))
                            {
                                foreach (var response in responses.EnumerateObject())
                                {
                                    if (response.Value.TryGetProperty("content", out JsonElement content))
                                    {
                                        if (content.ValueKind == JsonValueKind.Object)
                                        {
                                            foreach (var mediaType in content.EnumerateObject())
                                            {
                                                if (mediaType.Value.TryGetProperty("schema", out JsonElement schema))
                                                {
                                                    if (schema.ValueKind == JsonValueKind.Object &&
                                                        schema.TryGetProperty("properties", out JsonElement properties) &&
                                                        properties.ValueKind == JsonValueKind.Object)
                                                    {
                                                        foreach (var prop in properties.EnumerateObject())
                                                        {
                                                            if (prop.Name.Contains("access_token", StringComparison.OrdinalIgnoreCase) ||
                                                                prop.Name.Contains("token", StringComparison.OrdinalIgnoreCase))
                                                            {
                                                                endpoint.IsLoginEndpoint = true;
                                                                break;
                                                            }
                                                        }
                                                    }
                                                }
                                                if (endpoint.IsLoginEndpoint)
                                                    break;
                                            }
                                        }
                                    }
                                    if (endpoint.IsLoginEndpoint)
                                        break;
                                }
                            }

                            // Além da resposta, analisa o path em busca de palavras-chave (ex.: "login", "auth", "autentic")
                            if (!endpoint.IsLoginEndpoint)
                            {
                                var keywords = new[] { "login", "auth", "autentic" };
                                if (keywords.Any(k => path.ToLower().Contains(k)))
                                {
                                    endpoint.IsLoginEndpoint = true;
                                }
                            }

                            endpoints.Add(endpoint);
                        }
                    }
                }
                else
                {
                    FileHelper.WriteToFile("Propriedade 'paths' não encontrada no JSON do Swagger.");
                }
            }
            catch (Exception ex)
            {
                FileHelper.WriteToFile("Erro ao obter endpoints do Swagger: " + ex.Message);
            }

            return endpoints;
        }

        /// <summary>
        /// Extrai os parâmetros esperados do requestBody e gera dados fake conforme o tipo definido no Swagger.
        /// </summary>
        //private Dictionary<string, object> ParseRequestBodyParameters(JsonElement requestBodyElement, JsonDocument swaggerDoc)
        //{
        //    var parameters = new Dictionary<string, object>();

        //    if (requestBodyElement.ValueKind == JsonValueKind.Object &&
        //        requestBodyElement.TryGetProperty("content", out JsonElement contentElement) &&
        //        contentElement.ValueKind == JsonValueKind.Object)
        //    {
        //        var parameters = ParseRequestBodySchema(requestBodyElement, swaggerDoc);
        //        result[path.Name] = parameters; // Armazena as propriedades do path com valores aleatórios


        //        foreach (var mediaType in contentElement.EnumerateObject())
        //        {
        //            if (mediaType.Value.TryGetProperty("schema", out JsonElement schema))
        //            {
        //                if (schema.ValueKind == JsonValueKind.Object &&
        //                    schema.TryGetProperty("properties", out JsonElement properties) &&
        //                    properties.ValueKind == JsonValueKind.Object)
        //                {
        //                    foreach (var property in properties.EnumerateObject())
        //                    {
        //                        var fakeValue = FakeDataGenerator.GenerateFakeValue(property.Name, property.Value);
        //                        parameters[property.Name] = fakeValue;
        //                    }
        //                }
        //            }
        //        }
        //    }

        //    return parameters;
        //}

        public Dictionary<string, object> ParseRequestBodyParameters(JsonElement requestBodyElement, JsonDocument swaggerDoc)
        {
            var result = new Dictionary<string, object>();

            // Acessa o 'content' dentro do requestBody para pegar o schema
            if (requestBodyElement.TryGetProperty("content", out JsonElement contentElement) &&
                contentElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var mediaType in contentElement.EnumerateObject())
                {
                    if (mediaType.Value.TryGetProperty("schema", out JsonElement schema))
                    {
                        // Verifica se o schema tem a referência e processa
                        if (schema.TryGetProperty("$ref", out JsonElement refElement))
                        {
                            var schemaName = refElement.GetString().Split("/").Last(); // Extrai o nome do schema

                            // Localiza o schema em components/schemas
                            if (swaggerDoc.RootElement.TryGetProperty("components", out JsonElement components) &&
                                components.TryGetProperty("schemas", out JsonElement schemas) &&
                                schemas.TryGetProperty(schemaName, out JsonElement schemaProperties))
                            {
                                // Acessa as propriedades do schema e gera valores aleatórios
                                if (schemaProperties.TryGetProperty("properties", out JsonElement properties))
                                {
                                    foreach (var property in properties.EnumerateObject())
                                    {
                                        if (property.Name == "object")
                                        {
                                            var tt = "";
                                        }


                                        var fakeValue = FakeDataGenerator.GenerateFakeValue(property.Name, property.Value);
                                        result[property.Name] = fakeValue;
                                    }
                                }
                            }
                        }
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Extrai os parâmetros esperados do requestBody e gera dados fake conforme o tipo definido no Swagger.
        /// </summary>
        private Dictionary<string, object> ParseQueryParameters(JsonElement requestElement)
        {
            var parameters = new Dictionary<string, object>();

            if (requestElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var param in requestElement.EnumerateArray())
                {
                    if (param.TryGetProperty("name", out JsonElement nameElement) &&
                        param.TryGetProperty("schema", out JsonElement schema))
                    {
                        string paramName = nameElement.GetString();
                        object fakeValue = FakeDataGenerator.GenerateFakeValue(paramName, schema);

                        if (fakeValue != null)
                        {
                            parameters[paramName] = fakeValue;
                        }
                    }
                }
            }

            return parameters;
        }
    }

    /// <summary>
    /// Representa um endpoint extraído do Swagger.
    /// </summary>
    public class SwaggerEndpoint
    {
        public string Path { get; set; }
        public string HttpMethod { get; set; }
        /// <summary>
        /// Parâmetros do corpo da requisição (se definidos), já preenchidos com dados fake.
        /// </summary>
        public Dictionary<string, object> RequestBody { get; set; }
        public bool RequiresAuthentication { get; set; }
        public Dictionary<string, object> Parameter { get; set; }
        /// <summary>
        /// Considerado de login se retornar token (ex.: "access_token", "token") ou se o path conter palavras‑chave.
        /// </summary>
        public bool IsLoginEndpoint { get; set; }
    }

    /// <summary>
    /// Centraliza as requisições HTTP.
    /// </summary>
    public class HttpRequestManager
    {
        private readonly HttpClient _client;

        public HttpRequestManager()
        {
            _client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        }

        /// <summary>
        /// Realiza a requisição para o endpoint informado, concatenando a URL base e adicionando o header de autorização (se houver token).
        /// Retorna um objeto com informações do request e do response.
        /// </summary>
        public async Task<HttpRequestResult> SendRequestAsync(string baseUrl, SwaggerEndpoint endpoint, string token)
        {
            var queryParameter = string.Empty;
            if (endpoint.Parameter != null && endpoint.Parameter.Any())
            {
                var listTemp = new List<string>();
                foreach (var item in endpoint.Parameter)
                {
                    listTemp.Add($"{item.Key}={item.Value}");

                    endpoint.Path = endpoint.Path.Replace("{"+$"{item.Key}"+"", string.Empty);
                }

                queryParameter = "?" + string.Join("&", listTemp);
            }

            string url = baseUrl + endpoint.Path + queryParameter;
            var request = new HttpRequestMessage(new HttpMethod(endpoint.HttpMethod), url);
            string requestBody = string.Empty;

            if (!string.IsNullOrEmpty(token))
            {
                request.Headers.Add("Authorization", "Bearer " + token);
            }

            if (endpoint.RequestBody != null && endpoint.RequestBody.Any())
            {
                requestBody = JsonSerializer.Serialize(endpoint.RequestBody);
                request.Content = new StringContent(requestBody, Encoding.UTF8, "application/json");
            }

            var result = new HttpRequestResult();
            result.RequestInfo = $"Request: {request.Method} {request.RequestUri}\n";
            if (!string.IsNullOrEmpty(requestBody))
                result.RequestInfo += $"Request Body: {requestBody}\n";

            try
            {
                var response = await _client.SendAsync(request);
                string content = await response.Content.ReadAsStringAsync();
                result.StatusCode = (int)response.StatusCode;
                result.ReasonPhrase = response.ReasonPhrase;
                result.ResponseInfo = $"Response: Status Code: {result.StatusCode} ({result.ReasonPhrase})\nContent: {content}";
                result.JsonResponse = content;
            }
            catch (TaskCanceledException)
            {
                result.ResponseInfo = "Erro: A requisição excedeu o tempo limite.";
            }
            catch (Exception ex)
            {
                result.ResponseInfo = $"Erro ao enviar requisição: {ex.Message}";
            }
            return result;
        }
    }

    /// <summary>
    /// Representa o resultado de uma requisição HTTP.
    /// </summary>
    public class HttpRequestResult
    {
        public string RequestInfo { get; set; }
        public string JsonResponse { get; set; }
        public string ResponseInfo { get; set; }
        public int? StatusCode { get; set; }
        public string ReasonPhrase { get; set; }
        public bool IsSuccessStatusCode => StatusCode.HasValue && StatusCode >= 200 && StatusCode < 300;
    }

    /// <summary>
    /// Gerencia o token JWT utilizado nas requisições.
    /// </summary>
    public static class TokenManager
    {
        public static string JwtToken { get; set; }

        // Expressão regular para identificar um token JWT no formato header.payload.signature
        private static readonly Regex JwtRegex = new(@"^[A-Za-z0-9-_]+\.[A-Za-z0-9-_]+\.[A-Za-z0-9-_]+$", RegexOptions.Compiled);

        /// <summary>
        /// Extrai um token JWT de um JSON de resposta.
        /// </summary>
        /// <param name="responseContent">Conteúdo da resposta JSON.</param>
        /// <returns>O token JWT encontrado ou null caso não seja encontrado.</returns>
        public static string ExtractToken(string responseContent)
        {
            try
            {
                using var doc = JsonDocument.Parse(responseContent);
                return FindJwtToken(doc.RootElement); // Inicia a busca pelo token JWT no JSON
            }
            catch (Exception ex)
            {
                FileHelper.WriteToFile("Erro ao extrair token: " + ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Busca recursivamente um token JWT dentro da estrutura do JSON.
        /// </summary>
        /// <param name="element">Elemento do JSON a ser analisado.</param>
        /// <returns>O token JWT encontrado ou null caso não seja encontrado.</returns>
        private static string FindJwtToken(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.String)
            {
                string value = element.GetString();
                // Verifica se o valor corresponde ao formato de um JWT
                if (JwtRegex.IsMatch(value))
                    return value;
            }
            else if (element.ValueKind == JsonValueKind.Object)
            {
                // Percorre todas as propriedades do objeto JSON
                foreach (var property in element.EnumerateObject())
                {
                    string token = FindJwtToken(property.Value);
                    if (token != null)
                        return token; // Retorna imediatamente ao encontrar um JWT
                }
            }

            return null; // Retorna null caso nenhum JWT seja encontrado
        }
    }

    /// <summary>
    /// Gera dados fake com base no tipo e no formato definidos no Swagger.
    /// </summary>
    public static class FakeDataGenerator
    {
        private static readonly Faker faker = new Faker("pt_BR");

        public static object GenerateFakeValue(string propertyName, JsonElement propertySchema)
        {
            try
            {
                // Obtém o tipo; se não definido, assume "string"
                string type = propertySchema.TryGetProperty("type", out JsonElement typeElement)
                    ? typeElement.GetString()?.ToLower() ?? "string"
                    : "string";

                // Obtém o formato, se houver
                string format = propertySchema.TryGetProperty("format", out JsonElement formatElement)
                    ? formatElement.GetString()?.ToLower()
                    : string.Empty;

                switch (type)
                {
                    case "string":
                        if (!string.IsNullOrEmpty(format))
                        {
                            if (format == "uuid")
                                return Guid.NewGuid().ToString();
                            if (format == "date" || format == "date-time")
                                return DateTime.Now.ToString("o");
                        }
                        // Verifica se o nome da propriedade indica um dado específico
                        if (propertyName.ToLower().Contains("cpf"))
                            return GenerateCpfReal();
                        if (propertyName.ToLower().Contains("cnpj"))
                            return GenerateCnpjReal();
                        if (propertyName.ToLower().Contains("nome"))
                            return faker.Name.FullName();
                        if (propertyName.ToLower().Contains("placa"))
                            return GenerateCarPlate();
                        if (propertyName.ToLower().Contains("email"))
                            return faker.Person.Email;
                        if (propertyName.ToLower().Contains("seq"))
                            return faker.Random.Int(1, 50000).ToString();
                        if (propertyName.ToLower().Contains("cod"))
                            return faker.Random.Int(1, 50000).ToString();
                        if (propertyName.ToLower().Contains("dta"))
                            return DateTime.Now.ToString("o");
                        if (propertyName.ToLower().Contains("flg"))
                            return faker.Random.Bool();
                        // Valor default para string
                        return faker.Lorem.Word();
                    case "integer":
                        return faker.Random.Int(1, 50000);
                    case "number":
                        return faker.Random.Double();
                    case "boolean":
                        return faker.Random.Bool();
                    case "object":
                        if (propertyName.ToLower().Contains("seq"))
                            return faker.Random.Int(1, 50000).ToString();
                        if (propertyName.ToLower().Contains("cod"))
                            return faker.Random.Int(1, 50000).ToString();
                        if (propertyName.ToLower().Contains("dta"))
                            return DateTime.Now.ToString("o");
                        if (propertyName.ToLower().Contains("flg"))
                            return faker.Random.Bool();
                        return GenerateFakeObject(propertySchema);
                    case "array":
                        return new List<string> { faker.Commerce.Department(), faker.Commerce.Product() };
                    default:
                        return faker.Lorem.Word();
                }
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }

        private static object GenerateFakeObject(JsonElement propertyElement)
        {
            try
            {
                var fakeObject = new Dictionary<string, object>();

                // Verifica se a propriedade "properties" existe e é do tipo objeto
                if (propertyElement.TryGetProperty("properties", out JsonElement properties) && properties.ValueKind == JsonValueKind.Object)
                {
                    // Itera sobre as propriedades do objeto
                    foreach (var property in properties.EnumerateObject())
                    {
                        // Gera um valor falso para a propriedade, e caso tenha problemas, continua
                        try
                        {
                            fakeObject[property.Name] = GenerateFakeValue(property.Name, property.Value);
                        }
                        catch (Exception ex)
                        {
                            // Caso ocorra um erro ao gerar o valor para uma propriedade, registra
                            fakeObject[property.Name] = $"Erro ao gerar valor: {ex.Message}";
                        }
                    }
                }
                else
                {
                    // Caso não encontre "properties" ou se não for do tipo esperado
                    return $"Não foi possível identificar o tipo de dado.";
                }

                return fakeObject;
            }
            catch (Exception ex)
            {
                // Caso o erro ocorra fora do escopo do foreach, captura e retorna o erro
                return $"Erro ao gerar o objeto falso: {ex.Message}";
            }
        }

        public static string GenerateCpf() => faker.Random.Replace("###.###.###-##");
        public static string GenerateCnpj() => faker.Random.Replace("##.###.###/####-##");
        public static string GenerateCarPlate() => $"{faker.Random.String2(3, "ABCDEFGHIJKLMNOPQRSTUVWXYZ")}-{faker.Random.Number(1000, 9999)}";

        public static string GenerateCpfReal()
        {
            int[] cpf = new int[11];
            Random rnd = new Random();

            for (int i = 0; i < 9; i++)
                cpf[i] = rnd.Next(0, 9);

            cpf[9] = CalculateCpfDigit(cpf, 10);
            cpf[10] = CalculateCpfDigit(cpf, 11);

            return string.Join("", cpf);
        }

        public static string GenerateCnpjReal()
        {
            int[] cnpj = new int[14];
            Random rnd = new Random();

            for (int i = 0; i < 12; i++)
                cnpj[i] = rnd.Next(0, 9);

            cnpj[12] = CalculateCnpjDigit(cnpj, new int[] { 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2 });
            cnpj[13] = CalculateCnpjDigit(cnpj, new int[] { 6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2 });

            return string.Join("", cnpj);
        }

        private static int CalculateCpfDigit(int[] cpf, int factor)
        {
            int sum = 0;
            for (int i = 0; i < factor - 1; i++)
                sum += cpf[i] * (factor - i);

            int remainder = (sum * 10) % 11;
            return remainder == 10 ? 0 : remainder;
        }

        private static int CalculateCnpjDigit(int[] cnpj, int[] weights)
        {
            int sum = cnpj.Take(weights.Length).Select((n, i) => n * weights[i]).Sum();
            int remainder = sum % 11;
            return remainder < 2 ? 0 : 11 - remainder;
        }
    }

    public class FileHelper
    {
        public static void WriteToFile(string content)
        {
            try
            {
                Console.WriteLine(content);

                string currentDirectory = Directory.GetCurrentDirectory();

                // Cria o caminho completo do arquivo
                string filePath = Path.Combine(currentDirectory, $"LOG-REQUISICOES_{DateTime.Now.ToString("dd-MM-yyyy")}");

                // Verifica se o arquivo existe
                if (File.Exists(filePath))
                {
                    // Se o arquivo já existir, adiciona o conteúdo na próxima linha
                    File.AppendAllText(filePath, Environment.NewLine + content);
                }
                else
                {
                    // Se o arquivo não existir, cria um novo arquivo e escreve o conteúdo
                    File.WriteAllText(filePath, content);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erro ao escrever no arquivo: {ex.Message}");
            }
        }
    }
}
