# Comparador de Endpoints entre Homologação e Produção

## 📚 Objetivo do Projeto
Este projeto tem como objetivo comparar os responses de endpoints entre dois ambientes distintos de uma API REST: **Homologação** e **Produção**. Ele realiza requisições idênticas nos dois ambientes e lista apenas os endpoints cujos responses apresentam diferenças, mesmo que mínimas (como variações de maiúsculas/minúsculas ou espaços).

## 🔧 Como Funciona
1. O sistema solicita ao usuário as **URLs dos JSONs do Swagger** para os ambientes de Homologação e Produção.
2. Ele faz o parsing desses JSONs para extrair a lista de **endpoints e seus respectivos métodos HTTP**.
3. Identifica os **endpoints de login** em ambos os ambientes e solicita ao usuário a escolha do endpoint correto.
4. Caso a API exija autenticação, solicita ao usuário as credenciais e obtém um token JWT.
5. Para cada endpoint encontrado, realiza requisições idênticas para **Homologação** e **Produção**:
   - O corpo da requisição (JSON) e os parâmetros de rota/query são mantidos idênticos.
   - Se um endpoint requer corpo JSON, são gerados dados aleatórios usando a biblioteca **Faker**.
6. Compara os responses e exibe as diferenças entre os ambientes, detalhando:
   - O endpoint consultado.
   - O response de Homologação.
   - O response de Produção.
   - As diferenças linha a linha.

## 📚 Estrutura do Código
### **1. `Program.cs`** (Ponto de Entrada)
- Solicita ao usuário as URLs do Swagger para Homologação e Produção.
- Faz o parsing dos JSONs e identifica os endpoints.
- Obtém credenciais de login, caso necessário.
- Realiza requisições comparativas entre os dois ambientes.
- Lista as diferenças encontradas entre os responses.

### **2. `SwaggerParser.cs`** (Extração de Endpoints)
- Obtém os endpoints a partir do Swagger JSON.
- Identifica se o endpoint requer autenticação.
- Gera exemplos de requisição para endpoints com corpo JSON.

### **3. `HttpRequestManager.cs`** (Gestão de Requisições)
- Realiza chamadas HTTP para os endpoints.
- Garante que as requisições para Homologação e Produção sejam idênticas.
- Adiciona o token JWT no cabeçalho, se necessário.

### **4. `TokenManager.cs`** (Autenticação)
- Extrai o token JWT do response de login.
- Valida se o token está correto.

### **5. `FakeDataGenerator.cs`** (Geração de Dados de Teste)
- Usa a biblioteca **Faker** para gerar dados aleatórios para o corpo das requisições.
- Suporta dados como CPF, CNPJ, nomes, e-mails, telefones, etc.

### **6. `FileHelper.cs`** (Gerenciamento de Logs)
- Salva os logs de execução em um arquivo `.log`.
- Exibe informações no console durante a execução.

## 🎯 Tecnologias Utilizadas
- **C#** (.NET 8+)
- **HttpClient** (para requisições HTTP)
- **System.Text.Json** (para manipulação de JSONs)
- **Bogus (Faker)** (para geração de dados aleatórios)
- **Regex** (para extração de tokens JWT)

## ⚡ Exemplo de Saída do Comparador
```plaintext
Executando requisição: GET /api/usuarios

Endpoints com diferenças entre Homologação e Produção:
----------------------------------------------------
Endpoint: GET /api/usuarios

Response Homolog:
{
    "nome": "João Silva",
    "idade": 30,
    "email": "joao@email.com"
}

Response Produção:
{
    "nome": "João silva",
    "idade": 30,
    "email": "joao@email.com"
}

Diferenças:
Linha 2:
Homolog: "nome": "João Silva"
Produção: "nome": "João silva"
```

## ✨ Benefícios
- **Automatiza a validação entre Homologação e Produção**.
- **Evita divergências não identificadas manualmente**.
- **Auxilia times de QA e DevOps na garantia de qualidade**.
- **Detecta alterações inesperadas nos responses da API**.

## ⚙ Como Executar
1. **Compilar o projeto**:
   ```sh
   dotnet build
   ```
2. **Executar o binário**:
   ```sh
   dotnet run
   ```
3. **Fornecer as URLs do Swagger** quando solicitado no console.
4. **Acompanhar os logs e relatórios de diferenças**.
---

