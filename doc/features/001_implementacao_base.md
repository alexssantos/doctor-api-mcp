Aqui está um documento de especificação completo em **Markdown**, estruturado para um cenário realista de microserviços em .NET 10, com foco em observabilidade, clareza de contratos e execução em múltiplos ambientes.

---

# 📦 Especificação de Microserviços — ProdutoAPI & PrecoAPI (.NET 10)

## 📖 Visão Geral

Este projeto consiste em **dois microserviços independentes**, implementados em **.NET 10**, com responsabilidades bem definidas:

* **ProdutoAPI** → responsável por gerenciar produtos
* **PrecoAPI** → responsável por gerenciar preços

### 🔗 Relação entre serviços

* O cliente **NÃO acessa diretamente a PrecoAPI**
* A **ProdutoAPI consulta a PrecoAPI** para enriquecer os dados de produto com preços

---

## 🧱 Arquitetura

```
[ Cliente ]
     |
     v
[ ProdutoAPI ] ---> [ PrecoAPI ]
     |
     v
[ PostgreSQL (Produtos) ]

[ PostgreSQL (Preços) ]
```

---

## 📁 Estrutura do Projeto

Um único repositório com múltiplos projetos:

```
/src
  /BuildingBlocks
    /Observability
    /Http (infra genérica apenas)
  /Services
    /ProdutoAPI
      /Integration
        /PrecoApi
          PriceClient.cs
          Contracts
            PriceResponse.cs   👈 contrato do consumidor
    /PrecoAPI
      /Contracts
        PriceResponse.cs       👈 contrato da API (público)
/docker
/k8s
```

### Pasta TTTP/
/Http
  HttpClientFactoryExtensions.cs
  DelegatingHandlers/
    CorrelationHandler.cs

#### Responsabilidade
Infra HTTP genérica
Telemetria
Correlation ID
Logging

👉 ZERO conhecimento de domínio

---

## ⚙️ Tecnologias Utilizadas

* .NET 10 (ASP.NET Core)
* PostgreSQL
* OpenTelemetry
* Jaeger (Tracing)
* Docker / Docker Compose
* Kubernetes (manifests)
* Scalar + OpenAPI (Swagger avançado)

---

## 📡 Observabilidade

### 🔍 OpenTelemetry

Ambos os serviços devem exportar:

* Traces
* Metrics
* Logs correlacionados

#### Trace completo
o trace deve mostrar a request e response feita entre as aplicações incluindo body. 


### 🔧 Configuração base

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing =>
    {
        tracing
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddEntityFrameworkCoreInstrumentation()
            .AddOtlpExporter();
    });
```

### 📊 Jaeger

* Utilizado para visualização de traces distribuídos
* Cada request Produto → Preço deve gerar **trace correlacionado**

---

## 🧩 ProdutoAPI

### 📌 Responsabilidade

* CRUD de produtos
* Orquestração com PrecoAPI

### 🗄️ Banco

PostgreSQL — tabela `products`

```sql
CREATE TABLE products (
    id UUID PRIMARY KEY,
    name TEXT NOT NULL,
    description TEXT,
    created_at TIMESTAMP
);
```

---

### 🌐 Endpoints

---

### 📍 GET /api/products

Retorna lista de produtos com preço agregado

#### 🔸 Response

```json
[
  {
    "id": "b3f1c1e2-1234-4b3f-a111-111111111111",
    "name": "Notebook Dell",
    "description": "Notebook i7 16GB RAM",
    "price": {
      "value": 5500.50,
      "currency": "BRL"
    }
  }
]
```

#### 🔸 Comportamento

* Consulta produtos no banco
* Para cada produto:

  * Faz chamada HTTP para PrecoAPI
* Junta os dados

---

### 📍 GET /api/products/{id}

#### 🔸 Response

```json
{
  "id": "b3f1c1e2-1234-4b3f-a111-111111111111",
  "name": "Notebook Dell",
  "description": "Notebook i7 16GB RAM",
  "price": {
    "value": 5500.50,
    "currency": "BRL"
  }
}
```

---

### 📍 POST /api/products

#### 🔸 Request

```json
{
  "name": "Notebook Dell",
  "description": "Notebook i7 16GB RAM"
}
```

#### 🔸 Response

```json
{
  "id": "uuid-gerado",
  "name": "Notebook Dell",
  "description": "Notebook i7 16GB RAM"
}
```

---

### 📍 PUT /api/products/{id}

Atualiza produto

---

### 📍 DELETE /api/products/{id}

Remove produto

---

## 💰 PrecoAPI

### 📌 Responsabilidade

* Gerenciar preços isoladamente

---

### 🗄️ Banco

PostgreSQL — tabela `prices`

```sql
CREATE TABLE prices (
    id UUID PRIMARY KEY,
    product_id UUID NOT NULL,
    value NUMERIC(10,2),
    currency TEXT,
    updated_at TIMESTAMP
);
```

---

### 🌐 Endpoints

---

### 📍 GET /api/prices/{productId}

#### 🔸 Response

```json
{
  "productId": "b3f1c1e2-1234-4b3f-a111-111111111111",
  "value": 5500.50,
  "currency": "BRL"
}
```

---

### 📍 POST /api/prices

#### 🔸 Request

```json
{
  "productId": "b3f1c1e2-1234-4b3f-a111-111111111111",
  "value": 5500.50,
  "currency": "BRL"
}
```

---

### 📍 PUT /api/prices/{productId}

Atualiza preço

---

### 📍 DELETE /api/prices/{productId}

Remove preço

---

## 🔄 Comunicação entre APIs

### 🔧 HttpClient tipado

```csharp
public class PriceClient
{
    private readonly HttpClient _http;

    public PriceClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<PriceDto?> GetPrice(Guid productId)
    {
        var response = await _http.GetAsync($"/api/prices/{productId}");
        if (!response.IsSuccessStatusCode)
            return null;

        return await response.Content.ReadFromJsonAsync<PriceDto>();
    }
}
```

---

## 📜 OpenAPI + Scalar

### 🔧 Requisitos

* Documentação rica
* Exemplos obrigatórios
* Descrição detalhada de cada campo

### 🧾 Exemplo de annotation

```csharp
/// <summary>
/// Cria um novo produto no sistema
/// </summary>
/// <remarks>
/// Exemplo de request:
/// 
///     POST /api/products
///     {
///         "name": "Notebook Dell",
///         "description": "Notebook i7 16GB RAM"
///     }
/// 
/// </remarks>
```

---

## 🐳 Docker Compose

```yaml
version: '3.8'

services:
  produtoapi:
    build: ./src/Services/ProdutoAPI
    ports:
      - "5001:80"
    depends_on:
      - postgres-produto
      - jaeger

  precoapi:
    build: ./src/Services/PrecoAPI
    ports:
      - "5002:80"
    depends_on:
      - postgres-preco

  postgres-produto:
    image: postgres
    environment:
      POSTGRES_DB: produto_db

  postgres-preco:
    image: postgres
    environment:
      POSTGRES_DB: preco_db

  jaeger:
    image: jaegertracing/all-in-one
    ports:
      - "16686:16686"
```

---

## ☸️ Kubernetes

### 📦 Deploy ProdutoAPI

```yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: produtoapi
spec:
  replicas: 2
  selector:
    matchLabels:
      app: produtoapi
  template:
    metadata:
      labels:
        app: produtoapi
    spec:
      containers:
        - name: produtoapi
          image: produtoapi:latest
          ports:
            - containerPort: 80
```

---

### 🌐 Service

```yaml
apiVersion: v1
kind: Service
metadata:
  name: produtoapi
spec:
  selector:
    app: produtoapi
  ports:
    - port: 80
      targetPort: 80
```

---

## 🔐 Boas Práticas Aplicadas

* Separação clara de responsabilidades (SRP)
* Comunicação via HTTP desacoplada
* Observabilidade distribuída
* Independência de banco
* Pronto para escalabilidade horizontal

---

## 🧪 Objetivo do Projeto

Este projeto foi desenhado para validar:

* Comunicação entre microserviços
* Observabilidade real (tracing distribuído)
* Deploy híbrido (Docker + Kubernetes)
* Design de APIs desacopladas
