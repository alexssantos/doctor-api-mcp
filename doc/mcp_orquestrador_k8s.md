# 📘 Especificação Técnica — AI Agent + MCP para Observabilidade de Microserviços (.NET + Kubernetes)

---

# 🎯 Objetivo

Criar um ambiente local (k3d / Kubernetes) com:

* APIs .NET instrumentadas
* Observabilidade completa (tracing, logs, métricas)
* MCP Server agregando contexto
* Agente de IA consultando o MCP

Capaz de responder:

* "O que essa API faz?"
* "Quem chama essa rota?"
* "De onde vem esse dado?"
* "Essa rota está saudável?"
* "Qual o fluxo completo dessa operação?"

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
Jaeger  Scalar   K8s API
   |
Prometheus + Loki
```

---

# 📦 Stack Tecnológica

| Camada          | Tecnologia          |
| --------------- | ------------------- |
| APIs            | .NET 10             |
| Observabilidade | OpenTelemetry       |
| Tracing         | Jaeger              |
| Logs            | Loki                |
| Metrics         | Prometheus          |
| Dashboard       | Grafana             |
| API Docs        | Scalar              |
| MCP Server      | .NET ou Go          |
| Infra           | Kubernetes (k3d)    |
| Packaging       | Helm                |

---

# 🚀 FASE 1 — APIs + Scalar (OpenAPI)

## 🎯 Objetivo

Disponibilizar APIs com:

* Scalar UI
* Estrutura rastreável

---

## 📁 Estrutura

```txt
/src
  /Services
    /ProductAPI
    /PriceAPI
  /BuildingBlocks
/k8s
/helm
```

---

## ⚙️ Configuração .NET

### Adicionar Scalar

```csharp
builder.Services.AddOpenApi();
```

```csharp
app.MapOpenApi();
app.MapScalarApiReference();
```

---

## 📌 Exposição

Endpoints esperados:

```txt
http://productapi.local/scalar/v1
http://priceapi.local/scalar/v1
http://productapi.local/openapi/v1.json
http://priceapi.local/openapi/v1.json
```

---

## ☸️ Kubernetes — Deployment mínimo

```yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: productapi
  namespace: mcp-apis
spec:
  replicas: 1
  selector:
    matchLabels:
      app: productapi
  template:
    spec:
      containers:
        - name: productapi
          image: productapi:latest
          ports:
            - containerPort: 8080
          env:
            - name: ASPNETCORE_ENVIRONMENT
              value: Development
---
apiVersion: v1
kind: Service
metadata:
  name: productapi
  namespace: mcp-apis
spec:
  selector:
    app: productapi
  ports:
    - port: 80
      targetPort: 8080
---
apiVersion: networking.k8s.io/v1
kind: Ingress
metadata:
  name: productapi-ingress
  namespace: mcp-apis
spec:
  ingressClassName: nginx
  rules:
    - host: productapi.local
      http:
        paths:
          - path: /
            pathType: Prefix
            backend:
              service:
                name: productapi
                port:
                  number: 80
```

---

## ✅ Resultado esperado

* APIs rodando como pods
* Scalar acessível via ingress ou port-forward
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
dotnet add package OpenTelemetry.Exporter.OpenTelemetryProtocol
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

## ☸️ Jaeger (Tracing Backend)

```yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: jaeger
  namespace: mcp-apis
spec:
  replicas: 1
  selector:
    matchLabels:
      app: jaeger
  template:
    spec:
      containers:
        - name: jaeger
          image: jaegertracing/all-in-one:1.57
          env:
            - name: COLLECTOR_OTLP_ENABLED
              value: "true"
          ports:
            - containerPort: 4317   # OTLP gRPC
            - containerPort: 16686  # UI / HTTP query
---
apiVersion: v1
kind: Service
metadata:
  name: jaeger
  namespace: mcp-apis
spec:
  selector:
    app: jaeger
  ports:
    - name: otlp-grpc
      port: 4317
      targetPort: 4317
    - name: ui
      port: 16686
      targetPort: 16686
```

---

## 🔗 Configurar exportação (ConfigMap da API)

```yaml
apiVersion: v1
kind: ConfigMap
metadata:
  name: productapi-config
  namespace: mcp-apis
data:
  Otel__Endpoint: "http://jaeger:4317"
  Otel__CaptureBody: "false"
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
apiVersion: apps/v1
kind: Deployment
metadata:
  name: prometheus
  namespace: mcp-apis
spec:
  replicas: 1
  selector:
    matchLabels:
      app: prometheus
  template:
    spec:
      containers:
        - name: prometheus
          image: prom/prometheus:latest
          ports:
            - containerPort: 9090
          volumeMounts:
            - name: config
              mountPath: /etc/prometheus
      volumes:
        - name: config
          configMap:
            name: prometheus-config
```

---

## Loki

```yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: loki
  namespace: mcp-apis
spec:
  replicas: 1
  selector:
    matchLabels:
      app: loki
  template:
    spec:
      containers:
        - name: loki
          image: grafana/loki:latest
          ports:
            - containerPort: 3100
```

---

## Grafana

```yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: grafana
  namespace: mcp-apis
spec:
  replicas: 1
  selector:
    matchLabels:
      app: grafana
  template:
    spec:
      containers:
        - name: grafana
          image: grafana/grafana:latest
          ports:
            - containerPort: 3000
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
/src
  /McpServer
    /Tools
    /Collectors
    /Index
```

---

## 🧠 Responsabilidades

### 1. Coletar OpenAPI via Scalar

```http
GET http://productapi/openapi/v1.json
```

---

### 2. Coletar traces (Jaeger API)

```http
GET http://jaeger:16686/api/services
GET http://jaeger:16686/api/traces?service=productapi
```

---

### 3. Coletar runtime (Kubernetes API)

```http
GET /api/v1/namespaces/mcp-apis/pods
GET /api/v1/namespaces/mcp-apis/services
```

> Usar `ServiceAccount` com permissões de leitura no namespace.

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

## ☸️ Kubernetes — MCP Server

```yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: mcp-server
  namespace: mcp-apis
spec:
  replicas: 1
  selector:
    matchLabels:
      app: mcp-server
  template:
    spec:
      serviceAccountName: mcp-reader
      containers:
        - name: mcp-server
          image: mcp-server:latest
          ports:
            - containerPort: 4000
          env:
            - name: ProductApi__BaseUrl
              value: "http://productapi"
            - name: PriceApi__BaseUrl
              value: "http://priceapi"
            - name: Jaeger__BaseUrl
              value: "http://jaeger:16686"
---
# ServiceAccount com acesso read-only ao namespace
apiVersion: v1
kind: ServiceAccount
metadata:
  name: mcp-reader
  namespace: mcp-apis
---
apiVersion: rbac.authorization.k8s.io/v1
kind: Role
metadata:
  name: mcp-reader-role
  namespace: mcp-apis
rules:
  - apiGroups: [""]
    resources: ["pods", "services", "endpoints"]
    verbs: ["get", "list"]
  - apiGroups: ["apps"]
    resources: ["deployments"]
    verbs: ["get", "list"]
---
apiVersion: rbac.authorization.k8s.io/v1
kind: RoleBinding
metadata:
  name: mcp-reader-binding
  namespace: mcp-apis
subjects:
  - kind: ServiceAccount
    name: mcp-reader
roleRef:
  kind: Role
  name: mcp-reader-role
  apiGroup: rbac.authorization.k8s.io
```

---

## ✅ Resultado esperado

* MCP expõe endpoints semânticos
* Acesso ao cluster via K8s API (sem Docker socket)
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
"Quais pods estão em CrashLoopBackOff?"
```

---

## Fluxo

```txt
User → Agent → MCP → Tools → Resposta
```

---

## 🧠 Estratégia

* Agent usa tool calling
* MCP responde com dados estruturados (pods, traces, OpenAPI)
* Agent monta resposta final

---

# 🔬 FASE 6 — Correlação Inteligente

## 🎯 Objetivo

Responder perguntas complexas:

> "Explique o fluxo completo do checkout"

---

## Combinação de dados

| Fonte      | Uso           |
| ---------- | ------------- |
| OpenAPI    | estrutura     |
| Tracing    | fluxo real    |
| Logs       | erros         |
| Métricas   | saúde         |
| K8s API    | estado do pod |

---

## Resultado

```txt
Client
  → productapi  (pod: productapi-6d8f9b-xxx)
    → priceapi  (pod: priceapi-7c4d1a-yyy)
      → postgres-produto
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

* "qual tabela essa rota usa?"
* "usa cache?"
* "publica evento?"

---

# ☸️ Helm — Estrutura final

```txt
/helm
  /productapi
    Chart.yaml
    values.yaml
    /templates
      deployment.yaml
      service.yaml
      ingress.yaml
      configmap.yaml
  /priceapi
    ...
  /mcp-server
    ...
  /observability        ← Fase 3
    /templates
      tempo.yaml
      prometheus.yaml
      loki.yaml
      grafana.yaml
```

Instalar tudo:

```bash
helm upgrade --install productapi helm/productapi -n mcp-apis
helm upgrade --install priceapi   helm/priceapi   -n mcp-apis
helm upgrade --install mcp-server helm/mcp-server -n mcp-apis
helm upgrade --install observability helm/observability -n mcp-apis
```

---

# 📈 Roadmap de Evolução

| Fase | Maturidade           |
| ---- | -------------------- |
| 1    | APIs + Scalar        |
| 2    | Tracing              |
| 3    | Observabilidade      |
| 4    | MCP                  |
| 5    | Agent                |
| 6    | Correlação           |
| 7    | Code Intelligence    |

---

# ⚠️ Principais Decisões Arquiteturais

## ❌ Evitar

* Basear tudo só no OpenAPI
* Ignorar tracing
* Não correlacionar dados
* Usar Docker socket dentro do cluster (preferir K8s API)

---

## ✅ Priorizar

* OpenTelemetry FIRST
* MCP como camada central
* Scalar como fonte de OpenAPI (endpoint `/openapi/v1.json`)
* K8s ServiceAccount com RBAC mínimo para runtime data
* Dados runtime + semânticos juntos

---

# 🧠 Insight Final

Você não está construindo:

> documentação inteligente

Você está construindo:

> **um "SRE Copilot + Arquiteto" do seu cluster Kubernetes**
