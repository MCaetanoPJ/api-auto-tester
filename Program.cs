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
            // Solicita as URLs dos Swagger JSON para Homologação e Produção
            Console.Write("Informe a URL do Swagger JSON da Homologação: ");
            string swaggerUrlHomolog = Console.ReadLine();
            Console.Write("Informe a URL do Swagger JSON da Produção: ");
            string swaggerUrlProd = Console.ReadLine();

            // Extrai as URLs base
            string baseUrlHomolog = GetBaseUrl(swaggerUrlHomolog);
            if (string.IsNullOrEmpty(baseUrlHomolog))
            {
                FileHelper.WriteToFile("URL base da Homologação inválida.");
                return;
            }
            string baseUrlProd = GetBaseUrl(swaggerUrlProd);
            if (string.IsNullOrEmpty(baseUrlProd))
            {
                FileHelper.WriteToFile("URL base da Produção inválida.");
                return;
            }

            // Obtém os endpoints definidos em cada Swagger
            var swaggerParser = new SwaggerParser();
            var endpointsHomolog = await swaggerParser.GetEndpointsAsync(swaggerUrlHomolog);
            var endpointsProd = await swaggerParser.GetEndpointsAsync(swaggerUrlProd);

            if (!endpointsHomolog.Any())
            {
                FileHelper.WriteToFile("Nenhum endpoint encontrado na API de Homologação.");
                return;
            }
            if (!endpointsProd.Any())
            {
                FileHelper.WriteToFile("Nenhum endpoint encontrado na API de Produção.");
                return;
            }

            // Lista e seleciona o endpoint de login para Homologação
            var loginEndpointsHomolog = endpointsHomolog.Where(ep => ep.IsLoginEndpoint).ToList();
            SwaggerEndpoint loginEndpointHomolog = null;
            if (loginEndpointsHomolog.Any())
            {
                FileHelper.WriteToFile("\nEndpoints identificados como login na Homologação:");
                for (int i = 0; i < loginEndpointsHomolog.Count; i++)
                {
                    FileHelper.WriteToFile($"{i + 1}. {loginEndpointsHomolog[i].HttpMethod} {loginEndpointsHomolog[i].Path}");
                }
                Console.Write("Informe o número do endpoint de login que deseja usar para Homologação: ");
                string loginSelectionInputHomolog = Console.ReadLine();
                if (!int.TryParse(loginSelectionInputHomolog, out int selectedIndexHomolog) ||
                    selectedIndexHomolog < 1 || selectedIndexHomolog > loginEndpointsHomolog.Count)
                {
                    FileHelper.WriteToFile("Seleção inválida. Encerrando.");
                    return;
                }
                loginEndpointHomolog = loginEndpointsHomolog[selectedIndexHomolog - 1];
            }
            else
            {
                FileHelper.WriteToFile("Nenhum endpoint de login foi identificado na Homologação.");
            }

            // Lista e seleciona o endpoint de login para Produção
            var loginEndpointsProd = endpointsProd.Where(ep => ep.IsLoginEndpoint).ToList();
            SwaggerEndpoint loginEndpointProd = null;
            if (loginEndpointsProd.Any())
            {
                FileHelper.WriteToFile("\nEndpoints identificados como login na Produção:");
                for (int i = 0; i < loginEndpointsProd.Count; i++)
                {
                    FileHelper.WriteToFile($"{i + 1}. {loginEndpointsProd[i].HttpMethod} {loginEndpointsProd[i].Path}");
                }
                Console.Write("Informe o número do endpoint de login que deseja usar para Produção: ");
                string loginSelectionInputProd = Console.ReadLine();
                if (!int.TryParse(loginSelectionInputProd, out int selectedIndexProd) ||
                    selectedIndexProd < 1 || selectedIndexProd > loginEndpointsProd.Count)
                {
                    FileHelper.WriteToFile("Seleção inválida. Encerrando.");
                    return;
                }
                loginEndpointProd = loginEndpointsProd[selectedIndexProd - 1];
            }
            else
            {
                FileHelper.WriteToFile("Nenhum endpoint de login foi identificado na Produção.");
            }

            var httpRequestManager = new HttpRequestManager();
            string tokenHomolog = await AuthenticateIfNeeded(httpRequestManager, baseUrlHomolog, loginEndpointHomolog, "Homologação");
            string tokenProd = await AuthenticateIfNeeded(httpRequestManager, baseUrlProd, loginEndpointProd, "Produção");

            // Percorre os endpoints (exceto os de login) e compara os responses entre as duas APIs
            var differences = new List<string>();
            foreach (var endpoint in endpointsHomolog)
            {
                if (endpoint.IsLoginEndpoint)
                    continue;

                // Procura endpoint correspondente na Produção
                var correspondingEndpoint = endpointsProd.FirstOrDefault(ep =>
                    ep.Path == endpoint.Path && ep.HttpMethod == endpoint.HttpMethod);
                if (correspondingEndpoint != null)
                {
                    // Garante que os dados enviados (JSON e route) sejam os mesmos
                    correspondingEndpoint.RequestBody = endpoint.RequestBody;
                    correspondingEndpoint.Parameter = endpoint.Parameter;

                    FileHelper.WriteToFile($"\nExecutando requisição: {endpoint.HttpMethod} {endpoint.Path}");
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

                    var resultHomolog = await httpRequestManager.SendRequestAsync(baseUrlHomolog, endpoint, tokenHomolog);
                    var resultProd = await httpRequestManager.SendRequestAsync(baseUrlProd, correspondingEndpoint, tokenProd);

                    if (!AreResponsesEqual(resultHomolog.JsonResponse, resultProd.JsonResponse))
                    {
                        string diff = GetDiff(resultHomolog.JsonResponse, resultProd.JsonResponse);
                        if (!string.IsNullOrEmpty(diff))
                        {
                            string report = $"Endpoint: {endpoint.HttpMethod} {endpoint.Path}\n\n" +
                                        $"Response Homolog (Route):\n{resultHomolog.Route}\n\n" +
                                        $"Response Homolog (Json Request):\n{resultHomolog.JsonRequest}\n\n" +
                                        $"Response Homolog (Response):\n{resultHomolog.JsonResponse}\n\n" +
                                        $"Response Produção (Route):\n{resultProd.Route}\n\n" +
                                        $"Response Produção (Json Request):\n{resultProd.JsonRequest}\n\n" +
                                        $"Response Produção (Response):\n{resultProd.JsonResponse}\n\n" +
                                        $"Diferenças:\n{diff}";
                            differences.Add(report);
                        }
                    }
                }
            }

            // Exibe os endpoints com diferenças, juntamente com os responses e o diff detalhado
            FileHelper.WriteToFile("\nRelatório de diferenças entre Homologação e Produção:");
            if (differences.Any())
            {
                foreach (var diffReport in differences)
                {
                    FileHelper.WriteToFile("----------------------------------------------------");
                    FileHelper.WriteToFile(diffReport);
                }
            }
            else
            {
                FileHelper.WriteToFile("Nenhuma diferença encontrada entre os endpoints.");
            }
        }

        /// <summary>
        /// Extrai a URL base (ex.: https://api.exemplo.com) a partir da URL completa.
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
        /// Realiza o fluxo de autenticação para o ambiente informado.
        /// Caso o endpoint de login possua parâmetros definidos, permite ao usuário atualizá-los;
        /// caso contrário, solicita um JSON de login.
        /// Repete a requisição de login até que ela seja bem-sucedida.
        /// </summary>
        private static async Task<string> AuthenticateIfNeeded(HttpRequestManager httpRequestManager, string baseUrl, SwaggerEndpoint loginEndpoint, string environment)
        {
            if (loginEndpoint == null)
            {
                Console.WriteLine($"Nenhum endpoint de login disponível para {environment}.");
                return null;
            }

            Console.WriteLine($"\nRealizando autenticação para {environment}.");
            if (loginEndpoint.RequestBody != null && loginEndpoint.RequestBody.Any())
            {
                Console.WriteLine("Parâmetros do endpoint de login (valores fake já gerados):");
                foreach (var kvp in loginEndpoint.RequestBody)
                {
                    Console.WriteLine($" - {kvp.Key}: {kvp.Value}");
                }
                Console.WriteLine("Informe os valores para os parâmetros. Para manter o valor atual, pressione Enter:");
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
                Console.WriteLine("O endpoint de login não possui parâmetros definidos no Swagger JSON.");
                Console.WriteLine("Por favor, informe um JSON preenchido com os dados de login a ser usado:");
                loginEndpoint.RequestBody = await ReadAndValidateJsonAsync();
            }

            HttpRequestResult loginResult;
            do
            {
                Console.WriteLine("Realizando requisição de login...");
                loginResult = await httpRequestManager.SendRequestAsync(baseUrl, loginEndpoint, null);
                Console.WriteLine("Resultado da requisição de login:");
                Console.WriteLine(loginResult.RequestInfo);
                Console.WriteLine(loginResult.ResponseInfo);
                if (!loginResult.IsSuccessStatusCode)
                {
                    Console.WriteLine($"Falha no login. Status Code: {loginResult.StatusCode} - {loginResult.ReasonPhrase}");
                    Console.WriteLine("Por favor, informe outro JSON para os dados de login:");
                    loginEndpoint.RequestBody = await ReadAndValidateJsonAsync();
                }
            } while (!loginResult.IsSuccessStatusCode);

            Console.WriteLine("Login realizado com sucesso.");
            string tokenExtracted = TokenManager.ExtractToken(loginResult.JsonResponse);
            if (!string.IsNullOrEmpty(tokenExtracted))
            {
                Console.WriteLine("Token armazenado com sucesso.");
            }
            else
            {
                Console.WriteLine("Login bem-sucedido, mas não foi possível extrair o token.");
            }
            return tokenExtracted;
        }

        /// <summary>
        /// Compara dois JSON (normalizando-os) para verificar se são iguais.
        /// Se a normalização falhar ou se um dos JSONs for nulo, utiliza comparação simples de strings.
        /// </summary>
        private static bool AreResponsesEqual(string json1, string json2)
        {
            if (string.IsNullOrWhiteSpace(json1) || string.IsNullOrWhiteSpace(json2))
            {
                return false;
            }

            try
            {
                using var doc1 = JsonDocument.Parse(json1);
                using var doc2 = JsonDocument.Parse(json2);
                var norm1 = JsonSerializer.Serialize(doc1, new JsonSerializerOptions { WriteIndented = true });
                var norm2 = JsonSerializer.Serialize(doc2, new JsonSerializerOptions { WriteIndented = true });
                return norm1 == norm2;
            }
            catch (Exception)
            {
                return json1.Trim() == json2.Trim();
            }
        }

        /// <summary>
        /// Retorna as diferenças entre dois JSON, comparando-os linha a linha.
        /// Mesmo diferenças mínimas serão listadas.
        /// </summary>
        private static string GetDiff(string json1, string json2)
        {
            // Se algum dos JSONs for nulo ou vazio, substitui por string vazia
            if (string.IsNullOrWhiteSpace(json1))
            {
                json1 = "";
            }
            if (string.IsNullOrWhiteSpace(json2))
            {
                json2 = "";
            }

            // Tenta normalizar os JSONs para formatação indentada
            try
            {
                using var doc1 = JsonDocument.Parse(json1);
                using var doc2 = JsonDocument.Parse(json2);
                json1 = JsonSerializer.Serialize(doc1, new JsonSerializerOptions { WriteIndented = true });
                json2 = JsonSerializer.Serialize(doc2, new JsonSerializerOptions { WriteIndented = true });
            }
            catch (Exception)
            {
                // Se não for possível normalizar, utiliza os valores originais
            }

            var lines1 = json1.Split('\n');
            var lines2 = json2.Split('\n');
            var diffBuilder = new StringBuilder();
            int maxLines = Math.Max(lines1.Length, lines2.Length);
            for (int i = 0; i < maxLines; i++)
            {
                string line1 = i < lines1.Length ? lines1[i] : "";
                string line2 = i < lines2.Length ? lines2[i] : "";
                if (line1 != line2)
                {
                    diffBuilder.AppendLine($"Linha {i + 1}:");
                    diffBuilder.AppendLine($"Homolog: {line1}");
                    diffBuilder.AppendLine($"Produção: {line2}");
                    diffBuilder.AppendLine();
                }
            }
            return diffBuilder.ToString();
        }

        /// <summary>
        /// Lê JSON do console de forma multiline até que o usuário informe uma linha vazia e valida se o JSON é válido.
        /// </summary>
        private static async Task<Dictionary<string, object>> ReadAndValidateJsonAsync()
        {
            Dictionary<string, object> loginData = null;
            while (loginData == null || !loginData.Any())
            {
                Console.WriteLine("Cole o JSON de login. Digite uma linha vazia para terminar:");
                string jsonInput = ReadMultilineInput();
                try
                {
                    loginData = JsonSerializer.Deserialize<Dictionary<string, object>>(jsonInput);
                    if (loginData == null || !loginData.Any())
                    {
                        Console.WriteLine("JSON fornecido está vazio ou inválido. Tente novamente.");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Erro ao interpretar o JSON fornecido: " + ex.Message);
                    Console.WriteLine("Tente novamente.");
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

                            // Verifica a existência de segurança (ex.: array \"security\")
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

                            // Verifica as respostas para identificar se o endpoint retorna um token
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

                            // Verifica palavras-chave no path para identificar endpoints de login
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
        public Dictionary<string, object> ParseRequestBodyParameters(JsonElement requestBodyElement, JsonDocument swaggerDoc)
        {
            var result = new Dictionary<string, object>();

            if (requestBodyElement.TryGetProperty("content", out JsonElement contentElement) &&
                contentElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var mediaType in contentElement.EnumerateObject())
                {
                    if (mediaType.Value.TryGetProperty("schema", out JsonElement schema))
                    {
                        if (schema.TryGetProperty("$ref", out JsonElement refElement))
                        {
                            var schemaName = refElement.GetString().Split('/').Last();

                            if (swaggerDoc.RootElement.TryGetProperty("components", out JsonElement components) &&
                                components.TryGetProperty("schemas", out JsonElement schemas) &&
                                schemas.TryGetProperty(schemaName, out JsonElement schemaProperties))
                            {
                                if (schemaProperties.TryGetProperty("properties", out JsonElement properties))
                                {
                                    foreach (var property in properties.EnumerateObject())
                                    {
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
        /// Extrai os parâmetros de query e gera valores fake conforme o tipo definido no Swagger.
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
        public Dictionary<string, object> RequestBody { get; set; }
        public bool RequiresAuthentication { get; set; }
        public Dictionary<string, object> Parameter { get; set; }
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
        /// </summary>
        public async Task<HttpRequestResult> SendRequestAsync(string baseUrl, SwaggerEndpoint endpoint, string token)
        {
            var queryParameter = string.Empty;
            if (endpoint.Parameter != null && endpoint.Parameter.Any())
            {
                var listTemp = new List<string>();
                foreach (var item in endpoint.Parameter)
                {
                    var query = "{" + item.Key + "}";
                    if (endpoint.Path.Contains(query))
                    {
                        endpoint.Path = endpoint.Path.Replace(query, item.Value.ToString());
                    }
                    else
                    {
                        listTemp.Add($"{item.Key}={item.Value}");
                    }
                }

                if (listTemp.Any())
                {
                    queryParameter = "?" + string.Join("&", listTemp);
                }
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
                result.JsonRequest = requestBody;
                result.Route = url;
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
        public string JsonRequest { get; set; }
        public string Route { get; set; }
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
        private static readonly Regex JwtRegex = new Regex("^[A-Za-z0-9-_]+\\.[A-Za-z0-9-_]+\\.[A-Za-z0-9-_]+$", RegexOptions.Compiled);

        /// <summary>
        /// Extrai um token JWT de um JSON de resposta.
        /// </summary>
        public static string ExtractToken(string responseContent)
        {
            try
            {
                using var doc = JsonDocument.Parse(responseContent);
                return FindJwtToken(doc.RootElement);
            }
            catch (Exception ex)
            {
                FileHelper.WriteToFile("Erro ao extrair token: " + ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Busca recursivamente um token JWT dentro do JSON.
        /// </summary>
        private static string FindJwtToken(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.String)
            {
                string value = element.GetString();
                if (JwtRegex.IsMatch(value))
                    return value;
            }
            else if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in element.EnumerateObject())
                {
                    string token = FindJwtToken(property.Value);
                    if (token != null)
                        return token;
                }
            }
            return null;
        }
    }

    /// <summary>
    /// Gera dados fake com base no tipo e formato definidos no Swagger.
    /// </summary>
    public static class FakeDataGenerator
    {
        private static readonly Faker faker = new Faker("pt_BR");

        public static object GenerateFakeValue(string propertyName, JsonElement propertySchema)
        {
            try
            {
                string type = propertySchema.TryGetProperty("type", out JsonElement typeElement)
                    ? typeElement.GetString()?.ToLower() ?? "string"
                    : "string";

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
                        if (propertyName.ToLower().Contains("data"))
                            return DateTime.Now.ToString("o");
                        if (propertyName.ToLower().Contains("flg"))
                            return faker.Random.Bool();
                        if (propertyName.ToLower().Contains("valor"))
                            return faker.Random.Int(1, 50000).ToString();
                        if (propertyName.ToLower().Contains("vlr"))
                            return faker.Random.Int(1, 50000).ToString();
                        if (propertyName.ToLower().Contains("ativo"))
                            return faker.Random.Bool();
                        if (propertyName.ToLower().Contains("status"))
                            return faker.Random.Bool();
                        if (propertyName.ToLower().Contains("celular"))
                            return faker.Person.Phone;
                        if (propertyName.ToLower().Contains("telefone"))
                            return faker.Person.Phone;
                        return faker.Lorem.Word();
                    case "integer":
                        return faker.Random.Int(1, 50000);
                    case "number":
                        return faker.Random.Double();
                    case "boolean":
                        return faker.Random.Bool();
                    case "object":
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
                        if (propertyName.ToLower().Contains("data"))
                            return DateTime.Now.ToString("o");
                        if (propertyName.ToLower().Contains("flg"))
                            return faker.Random.Bool();
                        if (propertyName.ToLower().Contains("valor"))
                            return faker.Random.Int(1, 50000).ToString();
                        if (propertyName.ToLower().Contains("vlr"))
                            return faker.Random.Int(1, 50000).ToString();
                        if (propertyName.ToLower().Contains("ativo"))
                            return faker.Random.Bool();
                        if (propertyName.ToLower().Contains("status"))
                            return faker.Random.Bool();
                        if (propertyName.ToLower().Contains("celular"))
                            return faker.Person.Phone;
                        if (propertyName.ToLower().Contains("telefone"))
                            return faker.Person.Phone;
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
                if (propertyElement.TryGetProperty("properties", out JsonElement properties) && properties.ValueKind == JsonValueKind.Object)
                {
                    foreach (var property in properties.EnumerateObject())
                    {
                        try
                        {
                            fakeObject[property.Name] = GenerateFakeValue(property.Name, property.Value);
                        }
                        catch (Exception ex)
                        {
                            fakeObject[property.Name] = $"Erro ao gerar valor: {ex.Message}";
                        }
                    }
                }
                else
                {
                    return "Não foi possível identificar o tipo de dado.";
                }
                return fakeObject;
            }
            catch (Exception ex)
            {
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
                string filePath = Path.Combine(currentDirectory, $"LOG-REQUISICOES_{DateTime.Now:dd-MM-yyyy}.log");
                if (File.Exists(filePath))
                {
                    File.AppendAllText(filePath, Environment.NewLine + content);
                }
                else
                {
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
