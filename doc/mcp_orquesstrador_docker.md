Abaixo está um **documento de especificação completo em Markdown**, focado em uma **implantação incremental via Docker Compose**, para evoluir até um agente de IA com MCP capaz de entender seu cluster e aplicações.

---

# 📘 Especificação Técnica — AI Agent + MCP para Observabilidade de Microserviços (.NET + Kubernetes-like via Docker Compose)

---

# 🎯 Objetivo

Criar um ambiente local (Docker Compose) que simule um cluster com:

* APIs .NET instrumentadas
* Observabilidade completa (tracing, logs, métricas)
* MCP Server agregando contexto
* Agente de IA consultando o MCP

Capaz de responder:

* “O que essa API faz?”
* “Quem chama essa rota?”
* “De onde vem esse dado?”
* “Essa rota está saudável?”
* “Qual o fluxo completo dessa operação?”

---

# 🧱 Arquitetura Geral

```txt
[ Agent (LLM) ]
        |
        v
[ MCP Server ]
   |   |   |
   v   v   v
Tracing  OpenAPI  Runtime
   |        |        |
 Tempo   Swagger   Docker APIs
   |
Prometheus + Loki
```

---

# 📦 Stack Tecnológica

| Camada          | Tecnologia     |
| --------------- | -------------- |
| APIs            | .NET 10        |
| Observabilidade | OpenTelemetry  |
| Tracing         | Tempo          |
| Logs            | Loki           |
| Metrics         | Prometheus     |
| Dashboard       | Grafana        |
| MCP Server      | .NET ou Go     |
| Infra           | Docker Compose |

---

# 🚀 FASE 1 — APIs + OpenAPI

## 🎯 Objetivo

Disponibilizar APIs com:

* Swagger/OpenAPI
* Estrutura rastreável

---

## 📁 Estrutura

```txt
/services
  /product-api
  /price-api
/docker-compose.yml
```

---

## ⚙️ Configuração .NET

### Adicionar OpenAPI

```csharp
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
```

```csharp
app.UseSwagger();
app.UseSwaggerUI();
```

---

## 📌 Exposição

Endpoints esperados:

```txt
http://product-api/swagger/v1/swagger.json
http://price-api/swagger/v1/swagger.json
```

---

## 🐳 docker-compose.yml

```yaml
version: '3.9'

services:
  product-api:
    build: ./services/product-api
    ports:
      - "5001:8080"
    environment:
      - ASPNETCORE_ENVIRONMENT=Development

  price-api:
    build: ./services/price-api
    ports:
      - "5002:8080"
```

---

## ✅ Resultado esperado

* APIs rodando
* Swagger acessível
* Base para ingestão do MCP

---

# 🔍 FASE 2 — Observabilidade (OpenTelemetry)

## 🎯 Objetivo

Capturar:

* traces
* chamadas HTTP
* banco de dados

---

## 📦 Dependências

```bash
dotnet add package OpenTelemetry.Extensions.Hosting
dotnet add package OpenTelemetry.Instrumentation.AspNetCore
dotnet add package OpenTelemetry.Instrumentation.Http
```

---

## ⚙️ Configuração

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing =>
    {
        tracing
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddOtlpExporter();
    });
```

---

## 🐳 Tempo (Tracing Backend)

```yaml
  tempo:
    image: grafana/tempo
    ports:
      - "3200:3200"
      - "4317:4317"
```

---

## 🔗 Configurar exportação

```bash
OTEL_EXPORTER_OTLP_ENDPOINT=http://tempo:4317
```

---

## ✅ Resultado esperado

* Cada request gera trace
* Comunicação entre APIs rastreável

---

# 📊 FASE 3 — Métricas e Logs

---

## Prometheus

```yaml
  prometheus:
    image: prom/prometheus
    volumes:
      - ./config/prometheus.yml:/etc/prometheus/prometheus.yml
    ports:
      - "9090:9090"
```

---

## Loki

```yaml
  loki:
    image: grafana/loki
    ports:
      - "3100:3100"
```

---

## Grafana

```yaml
  grafana:
    image: grafana/grafana
    ports:
      - "3000:3000"
```

---

## ✅ Resultado esperado

* Dashboard com:

  * latência
  * erros
  * throughput
* Logs centralizados

---

# 🔗 FASE 4 — MCP Server

## 🎯 Objetivo

Criar camada semântica para o agente.

---

## 📁 Estrutura

```txt
/mcp-server
  /tools
  /collectors
  /index
```

---

## 🧠 Responsabilidades

### 1. Coletar OpenAPI

```http
GET http://product-api/swagger/v1/swagger.json
```

---

### 2. Coletar traces (Tempo API)

```http
GET /api/search?service=product-api
```

---

### 3. Coletar runtime (Docker)

* containers
* status
* health

---

## 🔧 Tools do MCP

```txt
- list_services
- get_openapi
- trace_route
- explain_api
- get_health
- find_dependencies
- find_data_origin
```

---

## 🐳 docker-compose

```yaml
  mcp-server:
    build: ./mcp-server
    ports:
      - "4000:4000"
    depends_on:
      - product-api
      - price-api
      - tempo
```

---

## ✅ Resultado esperado

* MCP expõe endpoints semânticos
* Base para agente

---

# 🤖 FASE 5 — Agente de IA

## 🎯 Objetivo

Permitir perguntas como:

```txt
"Explique a API de produtos"
"Quem chama o price-api?"
"De onde vem o preço?"
"Essa rota está saudável?"
```

---

## Fluxo

```txt
User → Agent → MCP → Tools → Resposta
```

---

## 🧠 Estratégia

* Agent usa tool calling
* MCP responde com dados estruturados
* Agent monta resposta final

---

# 🔬 FASE 6 — Correlação Inteligente

## 🎯 Objetivo

Responder perguntas complexas:

> “Explique o fluxo completo do checkout”

---

## Combinação de dados

| Fonte    | Uso        |
| -------- | ---------- |
| OpenAPI  | estrutura  |
| Tracing  | fluxo real |
| Logs     | erros      |
| Métricas | saúde      |

---

## Resultado

```txt
Client
  → product-api
    → price-api
      → DB
```

---

# 🧩 FASE 7 — (Opcional) Code Intelligence

## 🎯 Objetivo

Entender código real

---

## Estratégia

* Roslyn (.NET)
* Indexação AST
* Mapear:

  * Controllers
  * Services
  * Repositories

---

## Permite responder

* “qual tabela essa rota usa?”
* “usa cache?”
* “publica evento?”

---

# 📦 docker-compose FINAL (simplificado)

```yaml
version: '3.9'

services:
  product-api:
    build: ./services/product-api
    ports:
      - "5001:8080"

  price-api:
    build: ./services/price-api
    ports:
      - "5002:8080"

  tempo:
    image: grafana/tempo
    ports:
      - "4317:4317"

  prometheus:
    image: prom/prometheus
    ports:
      - "9090:9090"

  loki:
    image: grafana/loki
    ports:
      - "3100:3100"

  grafana:
    image: grafana/grafana
    ports:
      - "3000:3000"

  mcp-server:
    build: ./mcp-server
    ports:
      - "4000:4000"
```

---

# 📈 Roadmap de Evolução

| Fase | Maturidade        |
| ---- | ----------------- |
| 1    | APIs + Swagger    |
| 2    | Tracing           |
| 3    | Observabilidade   |
| 4    | MCP               |
| 5    | Agent             |
| 6    | Correlação        |
| 7    | Code Intelligence |

---

# ⚠️ Principais Decisões Arquiteturais

## ❌ Evitar

* Basear tudo só em OpenAPI
* Ignorar tracing
* Não correlacionar dados

---

## ✅ Priorizar

* OpenTelemetry FIRST
* MCP como camada central
* Dados runtime + semânticos juntos

---

# 🧠 Insight Final

Você não está construindo:

> documentação inteligente

Você está construindo:

> **um “SRE Copilot + Arquiteto” do seu ambiente**

---

Se quiser, posso no próximo passo:

* gerar o **código inicial do MCP em .NET**
* criar **estrutura de tools**
* ou montar um **exemplo real de “trace_route” funcionando**
