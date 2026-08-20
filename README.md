# mcp-apis

Projeto de estudo que demonstra como expor observabilidade de um cluster Kubernetes para agentes de IA usando o **Model Context Protocol (MCP)**. Dois microsserviços .NET (PrecoAPI e ProdutoAPI) rodam em k3d com métricas, traces e logs. O McpServer correlaciona esses sinais em contratos normalizados de catálogo, saúde, dependências, anomalias, timeline de incidente e causa raiz explicável, sem executar ações nos workloads observados.

---

## Índice

- [Visão Geral da Arquitetura](#visão-geral-da-arquitetura)
- [Instalação rápida](#instalação-rápida)
- [Serviços](#serviços)
- [Stack Tecnológica](#stack-tecnológica)
- [Estrutura de Arquivos](#estrutura-de-arquivos)
- [MCP Server — Ferramentas](#mcp-server--ferramentas)
- [Configuração do MCP Server](#configuração-do-mcp-server)
- [Dashboard](#dashboard)
- [Observabilidade](#observabilidade)
- [Kubernetes](#kubernetes)
- [Scripts de Automação](#scripts-de-automação)
- [Decisões de Implementação](#decisões-de-implementação)
- [Portas e Acessos](#portas-e-acessos)
- [Configuração do Ambiente](#configuração-do-ambiente)
- [Desenvolvimento com Tilt](#desenvolvimento-com-tilt)

---

## Instalação rápida

Instale o MCP Server no contexto Kubernetes atual com uma única linha:

**Linux / WSL:**

```bash
curl -fsSL https://raw.githubusercontent.com/alexssantos/doctor-api-mcp/master/install.sh | bash
```

**PowerShell** — usa as ferramentas nativas ou delega para o WSL:

```powershell
irm https://raw.githubusercontent.com/alexssantos/doctor-api-mcp/master/install.ps1 | iex
```

**Helm:**

```bash
helm upgrade --install doctor-api-mcp oci://registry-1.docker.io/alexssantos/doctor-api-mcp-chart --version 0.1.1 -n mcp-apis --create-namespace
```

Depois, abra o acesso local:

```bash
kubectl port-forward service/doctor-api-mcp 4000:4000 -n mcp-apis
```

- Dashboard: `http://localhost:4000/dashboard`
- MCP: `http://localhost:4000/`
- Imagem: `alexssantos/doctor-api-mcp:latest`

Veja configuração de providers, autenticação, upgrade e desinstalação em [doc/installation.md](doc/installation.md).

O instalador também oferece os presets `cluster`, `namespace`, `no-volumes`, `no-service-discovery` e `restricted`. Eles ajustam RBAC, token do ServiceAccount, estado, volumes e ferramentas MCP como um único contrato. Veja a [matriz de restrições e testes](doc/006_infraestrutura_testes_clusters.md).

> Desenvolvimento local: o fluxo completo com APIs, bancos e observabilidade continua sendo `./infra/scripts/ps/up-k8s.ps1 -Build` no Windows + WSL/k3d.

### Build independente do MCP Server

O limite de build do produto é a solução `src/McpServer.slnx`. Ela contém somente o MCP Server, o building block de observabilidade usado por ele e seus testes:

```powershell
dotnet build .\src\McpServer.slnx --configuration Release
dotnet test .\src\McpServer.slnx --configuration Release
docker build -f .\src\Services\McpServer\Dockerfile -t doctor-api-mcp:local .
```

`src/mcp-apis.slnx` é a solução do laboratório de integração e inclui intencionalmente `PrecoAPI` e `ProdutoAPI`. Esses serviços também são injetados no MCP Server apenas pelos manifests de laboratório em `infra/k8s/aplicacao/mcpserver/configmap.yaml`; a configuração padrão do servidor não pré-cadastra nenhuma aplicação.

---

## Visão Geral da Arquitetura

```
┌─────────────────────────────────────────────────────────────────────┐
│  Cliente (VS Code / Claude / Cursor)                                │
│  ── MCP over HTTP ──────────────────────────────────────────────►  │
└─────────────────────┬───────────────────────────────────────────────┘
                      │  POST http://localhost:4000/
                      ▼
┌─────────────────────────────────────────────────────────────────────┐
│  McpServer  (port 4000)                                             │
│  • 16 ferramentas MCP por padrão: 8 legadas + 8 vNext              │
│  • Dashboard SRE React em /dashboard (5 fluxos operacionais)       │
│  • Descoberta automática de aplicações (deployments/rede/OTel)     │
│  • Providers tipados: Kubernetes, Prometheus, Jaeger, Loki, OpenAPI│
│  • Engines: health, dependency, anomaly, correlation e RCA         │
│  • Autenticação reader/admin, limites, cache e auditoria            │
└──────────┬───────────────────────────┬─────────────────────────────┬┘
           │                           │                             │
           ▼                           ▼                             ▼
┌──────────────────────┐   ┌──────────────────────┐   ┌──────────────────────┐
│  Kubernetes API      │   │  Jaeger             │   │  Prometheus          │
│  (in-cluster RBAC)   │   │  (port 16686)       │   │  (port 9090)         │
│                      │   │  OTLP (port 4317)   │   │  Scrape /metrics     │
└──────────────────────┘   └──────────────────────┘   └──────────────────────┘

┌──────────────────────────────────────────────────────────────────────┐
│  Namespace: mcp-apis                                                 │
│                                                                      │
│  ┌──────────────────┐   HTTP    ┌──────────────────────────────────┐ │
│  │  ProdutoAPI      │ ────────► │  PrecoAPI                        │ │
│  │  port 8080       │           │  port 8080                       │ │
│  │  PostgreSQL      │           │  PostgreSQL                      │ │
│  │  (produto_db)    │           │  (preco_db)                      │ │
│  └──────────────────┘           └──────────────────────────────────┘ │
│                                                                      │
│  Observabilidade:  Prometheus · Grafana · Loki · Promtail · Jaeger   │
└──────────────────────────────────────────────────────────────────────┘
```

**Fluxo de dados:**
1. ProdutoAPI recebe `GET /api/products` → busca produtos no PostgreSQL próprio
2. Para cada produto, chama `PrecoAPI GET /api/prices/{productId}` via `PriceClient` (HttpClient tipado)
3. Retorna produto enriquecido com preço
4. Ambas as APIs exportam traces OTLP para Jaeger e métricas Prometheus
5. O MCP Server (e seu Dashboard) usam esses dados para responder perguntas de LLMs e exibir informações em tempo real

---

## Serviços

### PrecoAPI

Responsável exclusivamente pelo gerenciamento de preços de produtos.

| Endpoint | Método | Descrição |
|---|---|---|
| `/api/prices/{productId}` | GET | Busca preço por produto |
| `/api/prices` | POST | Cria entrada de preço |
| `/api/prices/{productId}` | PUT | Atualiza preço |
| `/scalar/v1` | GET | UI interativa da API (Scalar) |
| `/openapi/v1.json` | GET | Spec OpenAPI — **obrigatório para indexação pelo MCP** |
| `/metrics` | GET | Métricas Prometheus |

**Banco:** PostgreSQL separado (`preco_db`). Modelo: `Price { Id, ProductId, Value, Currency, UpdatedAt }`.

### ProdutoAPI

Gerencia produtos e enriquece as respostas com dados de preço vindos da PrecoAPI.

| Endpoint | Método | Descrição |
|---|---|---|
| `/api/products` | GET | Lista todos os produtos com preços |
| `/api/products/{id}` | GET | Produto por ID com preço |
| `/api/products` | POST | Cria produto |
| `/api/products/{id}` | PUT | Atualiza produto |
| `/api/products/{id}` | DELETE | Remove produto |
| `/scalar/v1` | GET | UI interativa da API (Scalar) |
| `/openapi/v1.json` | GET | Spec OpenAPI — **obrigatório para indexação pelo MCP** |
| `/metrics` | GET | Métricas Prometheus |

**Banco:** PostgreSQL separado (`produto_db`). Modelo: `Product { Id, Name, Description, CreatedAt }`.

**Integração:** usa `PriceClient` (HttpClient tipado) para chamar PrecoAPI. Falhas na PrecoAPI são tratadas graciosamente — o produto é retornado com `price: null`.

### McpServer

Servidor MCP que expõe o sistema para agentes de IA. Não possui banco de dados — é um orquestrador que agrega informações de:
- **Kubernetes API** (pods, services, deployments)
- **Jaeger** (traces, dependências)
- **OpenAPI** de cada serviço (rotas, schemas)

Transporte: **Streamable HTTP** via `POST /` (protocolo MCP 2025-03-26).

---

## Stack Tecnológica

### Aplicação

| Tecnologia | Versão | Uso |
|---|---|---|
| .NET / ASP.NET Core | 10 | Runtime de todas as APIs |
| Entity Framework Core | 10 | ORM, `EnsureCreatedAsync` para migrations simples |
| Npgsql.EFCore | 10 | Provider PostgreSQL |
| ModelContextProtocol.AspNetCore | 1.3.0 | SDK MCP para o McpServer |
| KubernetesClient | 19.0.2 | Consulta à Kubernetes API (in-cluster) |
| OpenTelemetry SDK | 1.17.0 | Tracing + Metrics exportados via OTLP/Prometheus |
| OpenTelemetry.Instrumentation.EFCore | 1.17.0-beta.1 | Instrumentação SQL sanitizada conforme semantic conventions |
| Scalar.AspNetCore | 2.5.0 | UI de documentação da API (substitui Swagger UI) |
| Microsoft.AspNetCore.OpenApi | 10 | Geração de spec OpenAPI nativa do .NET |

### Infraestrutura

| Tecnologia | Uso |
|---|---|
| k3d + k3s | Cluster Kubernetes local em Docker |
| Docker | Runtime dos containers; build local no k3d e imagem pública do McpServer no Docker Hub |
| Traefik v3 (nativo do K3s) | Roteamento local e afinidade de sessão sem controller externo |
| Kubernetes Gateway API (AKS) | Entrada TLS do ambiente alvo por controller gerenciado pela plataforma |
| Jaeger | Distributed tracing — recebe spans OTLP, expõe UI e API REST |
| Prometheus | Scraping de métricas dos endpoints `/metrics` |
| Grafana | Visualização de métricas e logs |
| Loki | Armazenamento de logs (schema v13, storage filesystem) |
| Promtail | Coleta de logs dos pods via DaemonSet |
| PostgreSQL 16 | Banco de dados — duas instâncias separadas (StatefulSet) |

---

## Estrutura de Arquivos

```
mcp-apis/
├── src/
│   ├── McpServer.slnx                    # Build independente do MCP Server + testes
│   ├── mcp-apis.slnx                     # Solução do laboratório de integração
│   ├── BuildingBlocks/
│   │   ├── Http/
│   │   │   ├── HttpClientFactoryExtensions.cs   # AddHttpClientWithCorrelation<T>
│   │   │   └── DelegatingHandlers/
│   │   │       └── CorrelationHandler.cs         # Propagação de correlation-id entre serviços
│   │   └── Observability/
│   │       ├── ObservabilityExtensions.cs        # AddObservability() — OTel tracing + metrics
│   │       ├── BodyCaptureOptions.cs             # Feature flag para captura de body
│   │       ├── RequestBodyTelemetryMiddleware.cs  # Captura body da request como span tag
│   │       └── ResponseBodyTelemetryMiddleware.cs # Captura body da response como span tag
│   └── Services/
│       ├── PrecoAPI/
│       │   ├── Program.cs                # Minimal API setup
│       │   ├── Controllers/
│       │   │   └── PricesController.cs   # CRUD de preços
│       │   ├── Models/Price.cs
│       │   ├── Data/PriceDbContext.cs
│       │   ├── Dtos/                     # Request/Response records
│       │   ├── Contracts/                # Interfaces de request
│       │   └── Dockerfile
│       ├── ProdutoAPI/
│       │   ├── Program.cs
│       │   ├── Controllers/
│       │   │   └── ProductsController.cs  # CRUD de produtos com enriquecimento de preço
│       │   ├── Models/Product.cs
│       │   ├── Data/ProductDbContext.cs
│       │   ├── Dtos/
│       │   ├── Integration/PrecoApi/
│       │   │   └── PriceClient.cs         # HttpClient tipado para PrecoAPI
│       │   └── Dockerfile
│       └── McpServer/
│           ├── Program.cs                 # MapMcp() + registro das ferramentas
│           ├── Services/
│           │   ├── KubernetesService.cs   # Wrapper do KubernetesClient (cluster-wide)
│           │   ├── JaegerService.cs       # Wrapper da Jaeger REST API
│           │   ├── OpenApiService.cs      # Fetch e parse de specs OpenAPI
│           │   ├── ApplicationCatalog.cs  # Inventário vivo de aplicações descobertas
│           │   ├── DiscoveryOrchestrator.cs      # Scan multi-fonte + correlação
│           │   ├── DiscoveryBackgroundService.cs # Re-scan periódico/sob demanda
│           │   └── KubernetesIndexingStateStore.cs # Persistência do toggle (ConfigMap)
│           ├── Tools/
│           │   ├── ListServicesTool.cs
│           │   ├── ListDiscoveredApplicationsTool.cs
│           │   ├── GetOpenApiTool.cs
│           │   ├── TraceRouteTool.cs
│           │   ├── ExplainApiTool.cs
│           │   ├── GetHealthTool.cs
│           │   ├── FindDependenciesTool.cs
│           │   ├── FindDataOriginTool.cs
│           │   └── ToolGuard.cs           # Gate central do switch de indexação
│           └── Dockerfile
│
├── k8s/                                   # Manifestos Kubernetes (kubectl apply -f)
│   ├── namespace.yaml
│   ├── aplicacao/                         # Serviços da aplicação (APIs + MCP Server)
│   │   ├── precoapi/                      # configmap, deployment, ingress, secret, service
│   │   ├── produtoapi/
│   │   └── mcpserver/                     # + rbac.yaml (ServiceAccount mcp-reader)
│   ├── banco/                             # Bancos de dados (PostgreSQL)
│   │   ├── postgres-preco/                # StatefulSet + configmap-init + secret + service
│   │   └── postgres-produto/
│   └── observabilidade/                   # Stack de observabilidade complementar ao MCP
│       ├── jaeger/
│       ├── prometheus/                    # + configmap com prometheus.yml
│       ├── loki/                          # + configmap com loki.yaml
│       ├── promtail/                      # DaemonSet
│       └── grafana/                       # + configmap-datasources.yaml
│
├── infra/
│   ├── k8s/                              # Manifests + overlays k3d/AKS
│   └── scripts/
│   ├── ps/                                # PowerShell (Windows)
│   │   ├── up-k8s.ps1                    # Sobe o ambiente completo
│   │   ├── down-k8s.ps1                  # Derruba o cluster
│   │   ├── port-forward.ps1              # Port-forwards para acesso local
│   │   ├── validate.ps1                  # Smoke tests de endpoints
│   │   ├── wsl-check.ps1                 # Valida ambiente WSL
│   │   └── wsl-run.ps1                   # Helper para executar no WSL
│   └── sh/                               # Shell (WSL/Linux)
│       ├── deploy-k8s.sh                 # Deploy via kubectl
│       ├── teardown-k8s.sh
│       ├── teardown-helm.sh
│       ├── port-forward.sh
│       ├── validate-phase3.sh
│       ├── validate-phase4.sh
│       └── validate-routes.sh
│
└── doc/
    ├── casos_de_uso.md
    ├── guia_configuracao_mcp.md
    ├── mcp_orquestrador_docker.md
    ├── mcp_orquestrador_k8s.md
    ├── tilt.md                            # Guia de desenvolvimento local com Tilt
    └── features/
        ├── 001_implementacao_base.md
        ├── 002_tracing_with_body.md
        ├── 003_service_discovery_and_validation.md
        └── 004_automatic_application_discovery.md
```

---

## MCP Server — Ferramentas

No perfil padrão `cluster`, o McpServer expõe **16 ferramentas**: oito legadas de compatibilidade e oito vNext com envelopes, limites, freshness, fontes e evidências. Todas recebem dependências via injeção de dependência.

| Ferramenta | Descrição | Fontes de Dados |
|---|---|---|
| `list_services` | Lista todos os services, pods e deployments no namespace com status | Kubernetes API + OpenApiService |
| `list_discovered_applications` | Inventário completo de aplicações auto-descobertas no cluster (fontes, estado do toggle, motivos de não-indexabilidade) | ApplicationCatalog |
| `get_openapi` | Retorna a spec OpenAPI completa de um serviço | HTTP → `/openapi/v1.json` |
| `trace_route` | Busca traces recentes de um serviço/rota com call chain e timings | Jaeger REST API |
| `explain_api` | Explica o que uma API faz combinando OpenAPI + dados de traces recentes | OpenAPI + Jaeger |
| `get_health` | Verifica a saúde de um serviço via estado dos pods (ready, restarts) | Kubernetes API |
| `find_dependencies` | Mapa de dependências entre serviços usando o grafo do Jaeger | Jaeger `/api/dependencies` |
| `find_data_origin` | Rastreia a origem dos dados de uma rota: API → chamadas HTTP → queries SQL | OpenAPI + Jaeger + Kubernetes |
| `service_get_spec` | Consolida identidade, versão, imagem, cobertura e endpoints | Catálogo + OpenAPI + Kubernetes |
| `service_get_health` | Avalia RED, estabilidade e recursos com score/findings | Prometheus + Kubernetes |
| `service_get_score` | Projeta o score de saúde para automações de leitura | Health Engine |
| `service_get_dependencies` | Retorna grafo normalizado e limitado por profundidade | Jaeger + Dependency Engine |
| `service_detect_anomalies` | Compara janela atual com baseline e classifica severidade | Prometheus + Anomaly Engine |
| `service_get_incident_timeline` | Correlaciona anomalias, traces, logs e deploy/events | Loki + Jaeger + Kubernetes |
| `service_find_root_cause` | Gera hipóteses explicáveis e recommendations não executáveis | Correlation + RCA Engine |
| `system_get_health_summary` | Resume a saúde dos serviços pelo mesmo engine do dashboard | Health Engine + catálogo |

> 🔒 **Superfície segura:** aplicações desabilitadas continuam protegidas pelo catálogo. `query_metrics` foi retirado da superfície padrão; PromQL bruto só pode existir nos endpoints administrativos quando `Observability:Features:EnableRawQueries=true`, com policy admin. O dashboard e as tools vNext consomem os mesmos providers/engines tipados.

Nos perfis restritos, a superfície acompanha as capacidades reais: sem service discovery, `list_services` é omitida; com `ClusterAccess__Scope=None`, `get_health` e `find_data_origin` também são omitidas. As ferramentas vNext continuam disponíveis e sinalizam fontes sem acesso como `partial`/`unavailable`.

### Registro das ferramentas

```csharp
builder.Services
    .AddMcpServer(opts => { opts.ServerInfo = new() { Name = "mcp-apis-server", Version = "1.0.0" }; })
    .WithHttpTransport()
    .WithTools<ListServicesTool>()
    // ...
```

Cada tool é uma classe estática com método `Execute` anotado com `[McpServerTool]`. Os parâmetros que são serviços do DI (ex: `IKubernetesCollector k8s`) são resolvidos automaticamente. Parâmetros primitivos viram argumentos da ferramenta visíveis ao LLM.

---

## Configuração do MCP Server

### Pipeline de inicialização

Ao subir, o McpServer executa automaticamente um scan de descoberta (e re-executa a cada 60s ou sob demanda):

```
DiscoveryOrchestrator (startup bloqueante + BackgroundService)
  └─► coleta apenas as fontes permitidas (Kubernetes no scope declarado, providers e Config)
       └─► correlação de identidade (matching estrutural + normalização de nomes)
              └─► Validation (ServiceValidator) — por aplicação com base URL
                     └─► estado do toggle (ConfigMap mcpserver-state)
                            └─► ApplicationCatalog.ReplaceSnapshot()
```

O `ApplicationCatalog` é a fonte de verdade: o dashboard lista **todas** as aplicações descobertas (inclusive as não indexáveis, com o motivo), e as tools MCP enxergam apenas as **habilitadas** no switch de indexação. Serviços que falham na validação OpenAPI seguem visíveis no dashboard e utilizáveis por tools de traces/health — apenas as tools baseadas em spec ficam indisponíveis.

### Restrições de acesso ao cluster

O bloco `ClusterAccess` declara quais capacidades podem ser usadas pelo processo e é refletido pelo chart Helm:

| Parâmetro | Valores | Efeito |
|---|---|---|
| `ClusterAccess__Scope` | `Cluster`, `Namespace`, `None` | Define RBAC cluster-wide, Role em um namespace ou ausência completa da API Kubernetes |
| `ClusterAccess__ServiceDiscovery` | `true`, `false` | Habilita Services/Endpoints; quando falso, força descoberta `Config` e exige `Services__*` |
| `ClusterAccess__StateStorage` | `ConfigMap`, `Memory` | Persiste estado no cluster ou somente no processo |
| `ClusterAccess__AllowVolumes` | `true`, `false` | Controla o `emptyDir` gravável em `/tmp`; o chart nunca cria PV/PVC |
| `ClusterAccess__ValidateOnStart` | `true`, `false` | Controla a validação antecipada no startup; `/ready` sempre valida e bloqueia requisitos ausentes |

`GET /api/requirements?refresh=true` retorna as permissões efetivas e os requisitos ausentes. Combinações inválidas são rejeitadas antes da instalação pelo schema Helm. Consulte [doc/006_infraestrutura_testes_clusters.md](doc/006_infraestrutura_testes_clusters.md) para os presets, invariantes e automação k3d/k6.

---

### Modos de descoberta

Controlado pela variável `Discovery__Mode` (ou `Discovery:Mode` no `appsettings.json`):

| Modo         | Fonte                                                                          |
|--------------|--------------------------------------------------------------------------------|
| `Auto` ⭐    | **Descoberta automática cluster-wide**: Deployments + Services/Endpoints (todos os namespaces) + serviços que emitem traces (Jaeger `/api/services`) + seção `Services` |
| `Config`     | Seção `Services` do appsettings / variáveis de ambiente `Services__<nome>`     |
| `Kubernetes` | Services no namespace com label `mcp-apis/indexed=true`                        |
| `Both`       | Mescla Config + label — K8s sobrescreve Config em caso de conflito de nome     |

**Padrão:** `Auto`. Aplicações auto-descobertas nascem **desabilitadas** para indexação (opt-in via switch no dashboard); as declaradas em `Services__*` ou com label `mcp-apis/indexed=true` nascem habilitadas. A label `mcp-apis/indexed=false` trava a aplicação como desabilitada (o switch fica bloqueado).

> 📄 Detalhes da correlação de identidade (deployment ↔ service ↔ OTel), persistência do toggle e semântica por tool: [`doc/features/004_automatic_application_discovery.md`](doc/features/004_automatic_application_discovery.md)

---

### Modo `Config`

Cada entrada em `Services` vira um serviço candidato. A **chave** é o alias que o MCP expõe para o LLM (aparece nas tools e nos logs); o **valor** é a URL real do Service K8s — os dois não precisam ser iguais:

```yaml
# infra/k8s/aplicacao/mcpserver/configmap.yaml
#
#   chave (alias MCP)          valor (URL do Service K8s)
#         ↓                              ↓
Services__pricing-api: "http://precoapi.mcp-apis.svc.cluster.local"
Services__product-api: "http://produtoapi.mcp-apis.svc.cluster.local"
Discovery__Mode:       "Config"
```

O LLM verá `pricing-api` e `product-api` nas ferramentas, enquanto o DNS resolve para os Services `precoapi` e `produtoapi` no namespace `mcp-apis`.

Use sempre FQDNs (`http://<service>.<namespace>.svc.cluster.local`) para garantir que o MCP Server alcance o serviço independentemente do namespace onde ele roda. URLs curtas (`http://precoapi`) só resolvem se o MCP Server estiver no **mesmo** namespace.

Para adicionar um serviço em outro namespace basta apontar para o FQDN correto:
```yaml
Services__minha-api: "http://minha-api.outro-namespace.svc.cluster.local"
```

---

### Modo `Kubernetes`

O McpServer lista todos os `Service` no namespace com a label configurada em `Discovery__KubernetesLabel` (padrão: `mcp-apis/indexed`) e valor `"true"`.

A URL base de cada serviço é resolvida em ordem:
1. Annotation `mcp-apis/base-url` no objeto `Service` (use FQDN para suporte multi-namespace)
2. Fallback: FQDN gerado automaticamente como `http://<name>.<namespace>.svc.cluster.local`

**Ativar o modo:**
```yaml
Discovery__Mode:           "Kubernetes"
Discovery__KubernetesLabel: "mcp-apis/indexed"
```

**Anotar um serviço para ser descoberto:**
```yaml
# infra/k8s/aplicacao/<meuservico>/service.yaml
metadata:
  labels:
    mcp-apis/indexed: "true"
  annotations:
    mcp-apis/base-url: "http://meuservico"
```

> ⚠️ Requer permissão `list` em `services` para o `ServiceAccount` do McpServer. O RBAC em `infra/k8s/aplicacao/mcpserver/rbac.yaml` já contempla essa permissão.

---

### Validação antes de indexar

Antes de registrar qualquer candidato, o `ServiceValidator` verifica três critérios. Se qualquer um falhar, o serviço é ignorado:

| # | Critério                          | Detalhe                                                                               |
|---|-----------------------------------|---------------------------------------------------------------------------------------|
| 1 | **Serviço acessível**             | Responde HTTP < 500 em `/health` ou `/` (timeout 10 s)                                |
| 2 | **OpenAPI spec acessível**        | Primeiro path de `DataSources:OpenApiSpecPaths` que retorne HTTP 200                  |
| 3 | **Spec com conteúdo válido**      | JSON parseável com pelo menos um `path` definido                                      |

O critério 2 testa os caminhos em ordem e usa o primeiro bem-sucedido. O caminho resolvido é gravado no `ServiceRegistry` e reutilizado em todas as chamadas da tool.

**Exemplo de log no startup:**
```
info: Service discovery (Config) found 2 candidate(s): precoapi, produtoapi
info: ✓ Registered service 'precoapi' at http://precoapi (spec: /openapi/v1.json)
warn: ✗ Skipped service 'produtoapi' at http://produtoapi: OpenAPI spec not found. Probed paths: /openapi/v1.json → HTTP 404, /swagger/v1/swagger.json → HTTP 404
info: Service discovery complete. 1 service(s) registered: precoapi
```

---

### Requisitos mínimos de uma API para ser indexada

Para que qualquer serviço passe na validação e seja exposto pelas tools do MCP, ele deve oferecer:

| Endpoint                           | Requisito                                                              |
|------------------------------------|------------------------------------------------------------------------|
| `/health` ou `/`                   | Responder HTTP < 500 (prova que o serviço está no ar)                  |
| Um dos `DataSources:OpenApiSpecPaths` | Retornar a spec OpenAPI em JSON com pelo menos um `path` definido   |

**No .NET com `Microsoft.AspNetCore.OpenApi`:**
```csharp
// Program.cs
builder.Services.AddOpenApi();
// ...
app.MapOpenApi(); // expõe /openapi/v1.json por padrão
app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));
```

Se o serviço expõe a spec em outro caminho (ex: `/swagger/v1/swagger.json`), basta que esse path esteja listado em `DataSources__OpenApiSpecPaths` no configmap — não é necessário alterar o serviço.

---

### Referência completa de configuração

| Variável de ambiente                     | Padrão                 | Descrição                                                       |
|------------------------------------------|------------------------|-----------------------------------------------------------------|
| `DataSources__Jaeger__BaseUrl`           | `http://jaeger:16686`  | URL da API REST do Jaeger                                       |
| `DataSources__Kubernetes__Namespace`     | `mcp-apis`             | Namespace onde o McpServer roda (estado + health padrão)        |
| `DataSources__OpenApiSpecPaths__0..N`    | `/openapi/v1.json`     | Caminhos candidatos para spec OpenAPI (primeiro 200 vence)      |
| `Discovery__Mode`                        | `Auto`                 | Fonte de descoberta: `Auto`, `Config`, `Kubernetes` ou `Both`   |
| `Discovery__KubernetesLabel`             | `mcp-apis/indexed`     | Label K8s de opt-in (`true`) / hard-off (`false`)               |
| `Discovery__RescanSeconds`               | `60`                   | Intervalo do re-scan periódico (`0` = só startup + manual)      |
| `Discovery__RevalidateSeconds`           | `300`                  | Reuso de validações OpenAPI bem-sucedidas                       |
| `Discovery__ForgetAfterMinutes`          | `60`                   | Tempo até esquecer aplicações não vistas pelos scans            |
| `Discovery__StateConfigMap`              | `mcpserver-state`      | ConfigMap que persiste o switch de indexação                    |
| `Discovery__ExcludeNamespaces__0..N`     | `kube-system`, ...     | Namespaces nunca escaneados                                     |
| `Discovery__ExcludeApps__0..N`           | `mcpserver`, `jaeger`, ... | Infra excluída do catálogo de aplicações                    |
| `Discovery__ExcludeOtelServices__0..N`   | `jaeger-query`, `McpServer` | Nomes OTel ignorados na descoberta                         |
| `Services__<nome>`                       | —                      | URL base de um serviço (habilitado por padrão em qualquer modo) |

> 📄 Documentação detalhada: [`doc/features/003_service_discovery_and_validation.md`](doc/features/003_service_discovery_and_validation.md) e [`doc/features/004_automatic_application_discovery.md`](doc/features/004_automatic_application_discovery.md)

---

## Dashboard

O McpServer também expõe um **Dashboard web interativo** em `/dashboard` que oferece uma interface visual para monitorar o cluster, visualizar métricas e acessar ferramentas de observabilidade.

### Acesso

| Ambiente | URL |
|---|---|
| Desenvolvimento (Tilt) | `http://localhost:4000/dashboard` |
| Kubernetes (Ingress) | `http://mcpserver.local:8080/dashboard` |
| Port-forward | `http://localhost:4000/dashboard` |

### Funcionalidades

O dashboard foi reorganizado em cinco fluxos operacionais. Ele não recebe
PromQL/LogQL do navegador; todas as decisões vêm dos mesmos engines das tools.

1. **Visão geral** — estado sistêmico, score/coverage, serviços críticos,
   findings, fontes, freshness, pods e deployments.
2. **Serviço** — gauge de saúde, dimensões, evidências, anomalias, grafo de
   dependências limitado, versão/imagem e resumo OpenAPI.
3. **Incidente** — timeline normalizada, hipótese de causa raiz, confidence,
   evidências contraditórias e recommendations sempre `executable=false`.
4. **Catálogo** — inventário namespace-aware, cobertura por sinal, validação
   OpenAPI e toggle administrativo persistido em `mcpserver-state`.
5. **Projeto** — arquitetura, oito tools vNext, postura de segurança e fluxo de
   execução local WSL/k3d.

O header mantém refresh, timestamp, links externos e tema claro/escuro
persistente. A interface possui skip-link, foco visível, headings semânticos,
alvos de toque de 44 px, tabelas acessíveis e layout responsivo sem overflow na
largura móvel.

### Stack Técnico

**Frontend (em `src/Services/McpServer/dashboard/`):**
- **React 19** + TypeScript 5
- **Vite 8** (rolldown) — bundler com dev server + build otimizado
- **Tailwind CSS v4** com `@tailwindcss/vite` plugin — CSS-first, tokens OKLCH, dark mode
- **Shadcn-style components** — componentes hand-built seguindo padrões shadcn (cva variants, Radix primitives)
- **TanStack React Query v5** — gerenciamento de estado das requisições, refetch automático a cada 15s
- **Playwright** — testes E2E de acessibilidade, dark mode, responsividade e console
- **Lucide React** — ícones (Activity, RefreshCw, Moon, Sun, Server, HeartPulse, etc.)

**Backend (em `src/Services/McpServer/`):**
- **Providers/engines vNext** — projeções tipadas, limites e falha parcial explicável
- **DashboardEndpoints.cs / ApplicationsEndpoints.cs** — rotas REST normalizadas:
  - `GET /api/dashboard/overview` — resumo sistêmico + cluster
  - `GET /api/dashboard/intelligence/system` — envelope do System Health Engine
  - `GET /api/dashboard/intelligence/services/{service}/spec`
  - `GET /api/dashboard/intelligence/services/{service}/health`
  - `GET /api/dashboard/intelligence/services/{service}/dependencies`
  - `GET /api/dashboard/intelligence/services/{service}/anomalies`
  - `GET /api/dashboard/intelligence/services/{service}/timeline`
  - `GET /api/dashboard/intelligence/services/{service}/root-cause`
  - `GET /api/dashboard/applications` — inventário e capacidade administrativa
  - `PUT /api/dashboard/applications/{name}/indexing` — policy admin
  - `POST /api/dashboard/discovery/rescan` — policy admin
  - `GET /api/dashboard/links` — URLs browser-facing
- **Static Files:** Vite build output em `wwwroot/dashboard/`, servido via `UseDefaultFiles()` + `UseStaticFiles()` + SPA fallback routes

### Configuração

**Em `appsettings.json` (credenciais do servidor):**
```json
{
  "DataSources": {
    "Prometheus": {
      "BaseUrl": "http://prometheus.mcp-apis.svc.cluster.local:9090"
    }
  },
  "Dashboard": {
    "Links": {
      "Jaeger": "http://jaeger.local:8080",
      "Prometheus": "http://prometheus.local:8080",
      "Grafana": "http://grafana.local:8080"
    }
  }
}
```

> ℹ️ As URLs em `DataSources.*` são **server-side** (cluster-internal, FQDNs) para o backend acessar as APIs.
> As URLs em `Dashboard:Links` são **browser-facing** (passadas ao frontend) — devem ser acessíveis do seu navegador host (via Ingress/port-forward).

**Em `infra/k8s/aplicacao/mcpserver/configmap.yaml`:**
```yaml
data:
  DataSources__Prometheus__BaseUrl: "http://prometheus.mcp-apis.svc.cluster.local:9090"
  Dashboard__Links__Jaeger: "http://jaeger.local:8080"
  Dashboard__Links__Prometheus: "http://prometheus.local:8080"
  Dashboard__Links__Grafana: "http://grafana.local:8080"
```

### Desenvolvimento Local

**Build do frontend:**
```bash
cd src/Services/McpServer/dashboard
npm ci
npm run build      # output em ../wwwroot/dashboard
```

**Dev server com hot reload:**
```bash
npm run dev        # localhost:5173, proxy /api/dashboard → localhost:4000
```

Após `npm run dev`, abra o navegador em `http://localhost:5173` — o Vite vai proxificar as chamadas de API para o backend em `localhost:4000` (configure `-CaptureBody` ou Tilt para que o McpServer esteja rodando em background).

**Build Docker:**
O `Dockerfile` do McpServer inclui um estágio `dashboard-build` (node:22-alpine) que constrói o React e overlay o output (`wwwroot/dashboard`) no estágio SDK do .NET antes do `dotnet publish`.

---

## Observabilidade

### OpenTelemetry

Ambas as APIs (PrecoAPI e ProdutoAPI) usam o BuildingBlock `Observability` que centraliza a configuração de tracing e métricas:

```csharp
builder.Services.AddObservability("PrecoAPI", builder.Configuration);
```

**Tracing (→ Jaeger via OTLP gRPC porta 4317):**
- Instrumentação automática: ASP.NET Core, HttpClient, Entity Framework Core
- `RecordException = true` — exceções aparecem nos spans
- EF Core 1.13+ emite texto SQL sanitizado por padrão; valores de parâmetros permanecem desativados para evitar PII

**Métricas (→ Prometheus via scraping do `/metrics`):**
- Instrumentação automática: ASP.NET Core, HttpClient, Runtime .NET
- `MapPrometheusScrapingEndpoint()` expõe o endpoint

**Captura de Body (feature flag):**

Ativada via `Otel:CaptureBody: true` no ConfigMap ou pelo parâmetro `-CaptureBody` do script de deploy:

```csharp
app.UseBodyCaptureTelemetry(); // registra Request + Response body middleware
```

Os bodies são limitados por `Otel:MaxCapturedBodyBytes` (16 KiB por padrão), passam por redaction de campos sensíveis e são adicionados como tags nos spans somente quando a feature está ativa.

### Propagação de Correlation ID

O `CorrelationHandler` (DelegatingHandler) propaga automaticamente o `traceparent` (W3C Trace Context) e o `X-Correlation-Id` nas chamadas HTTP de ProdutoAPI → PrecoAPI. Isso garante que a chamada apareça como um único trace distribuído no Jaeger.

### Stack de Observabilidade

```
PrecoAPI/ProdutoAPI
   │ OTLP gRPC :4317
   ▼
Jaeger (:16686)         ← traces e dependency graph
   
PrecoAPI/ProdutoAPI
   │ Prometheus scrape /metrics
   ▼
Prometheus (:9090)      ← métricas de request rate, latência, runtime

Promtail (DaemonSet)
   │ coleta logs dos pods
   ▼
Loki (:3100)            ← armazenamento de logs

Grafana (:3000)         ← dashboards para Prometheus + Loki
```

---

## Kubernetes

### Namespace

Todos os recursos vivem no namespace `mcp-apis`.

### Databases (StatefulSet)

Duas instâncias PostgreSQL 16 independentes, cada uma com:
- `StatefulSet` com 1 réplica e `PersistentVolumeClaim`
- `ConfigMap` com script SQL de inicialização do schema
- `Secret` com credenciais (base64)
- `Service` ClusterIP

### APIs (Deployment)

PrecoAPI e ProdutoAPI com:
- `replicas: 2`
- `imagePullPolicy: Never` — imagens buildadas localmente e importadas com `k3d image import`
- Resources com `requests` e `limits` definidos
- `readinessProbe` e `livenessProbe` via HTTP no `/health`
- `ConfigMap` para variáveis de ambiente (endpoint OTLP, URL da PrecoAPI, etc.)
- `Secret` para connection string do banco

### Ingress

Traefik nativo do K3s no laboratório k3d com:
- Host-based routing: `precoapi.local`, `produtoapi.local`
- IngressClass `traefik` e rotas `PathPrefix`
- cookie affinity `mcp-route` no Service do McpServer
- k3d expõe as portas `8080:80` e `8443:443` via loadbalancer no host

O overlay AKS remove o `Ingress` local e publica uma `HTTPRoute` para um Gateway
API suportado e administrado pela plataforma; Traefik também pode ser o
controller desse Gateway quando aprovado no ambiente alvo.

### RBAC do McpServer

```yaml
scope=Cluster
  ClusterRole: pods, deployments e eventos; Services/Endpoints quando habilitados
  Role: get/update/patch somente no ConfigMap de estado

scope=Namespace
  Role: os mesmos recursos, apenas no namespace da release

scope=None
  nenhum Role, ClusterRole ou binding; automountServiceAccountToken=false
```

Nos scopes `Cluster` e `Namespace`, o McpServer usa `KubernetesClientConfiguration.InClusterConfig()` e recebe o token projetado do ServiceAccount. A única permissão de escrita do runtime é `update/patch` no ConfigMap de estado, restrita por `resourceNames`. Em `scope=None`, o cliente Kubernetes nem é construído e o estado é mantido em memória.

O McpServer roda com duas réplicas, PDB, NetworkPolicy, rollout sem indisponibilidade, probes `/ready` e `/live`, filesystem read-only e afinidade de sessão no Ingress. Detalhes e matriz de validação: [`doc/operations/observability-intelligence-rollout.md`](doc/operations/observability-intelligence-rollout.md).

---

## Scripts de Automação

### `infra/scripts/ps/up-k8s.ps1` (principal)

Sobe o ambiente completo do zero ou a partir de um estado parcial. Cada etapa é
idempotente em clusters atuais. Clusters legados sem Traefik ou abaixo do K3s
1.36 são bloqueados antes do apply para exigir migração com preservação de dados.

```
Seção 0 — Valida ambiente WSL (wsl-check.ps1)
Seção 1 — Cria/inicia cluster k3d
Seção 2 — Aguarda e valida o Traefik nativo do K3s
Seção 3 — Build Docker + k3d image import  (habilitado com -Build)
Seção 4 — kubectl apply dos manifests e overlay k3d
Seção 5 — kubectl rollout status de todos os workloads
Seção 6 — Health check completo com port-forwards  (pulável com -SkipHealthCheck)
```

Parâmetros:
```powershell
.\infra\scripts\ps\up-k8s.ps1                   # aplica imagens já importadas
.\infra\scripts\ps\up-k8s.ps1 -Build            # build WSL + import + deploy
.\infra\scripts\ps\up-k8s.ps1 -SkipHealthCheck  # pula verificação final
.\infra\scripts\ps\up-k8s.ps1 -CaptureBody      # habilita captura de body no OTEL
```

**Detalhe de implementação:** Todos os comandos `k3d`, `kubectl` e `docker` são executados via WSL (instalados apenas no WSL, não no Windows). O helper `RunInWSL` usa `wsl.exe -- bash -lc` (login shell para carregar `~/.local/bin` no PATH).

### `infra/scripts/ps/wsl-check.ps1`

Valida o ambiente WSL antes do deploy: versão do WSL, distro, tools disponíveis, Docker daemon. Executado automaticamente pela seção 0 do `up-k8s.ps1`.

### `infra/scripts/ps/port-forward.ps1`

Inicia todos os port-forwards para acesso local sem precisar subir o ambiente completo.

### `infra/scripts/ps/validate.ps1`

Smoke tests: chama os endpoints das APIs, verifica Prometheus, Grafana, Jaeger e o protocolo MCP (initialize handshake).

---

## Decisões de Implementação

### Por que dois bancos PostgreSQL separados?

Isolamento de dados entre serviços — reflete o padrão de microsserviços onde cada serviço é dono do seu schema. ProdutoAPI não acessa diretamente o banco de PrecoAPI; a integração é sempre via HTTP.

### Por que `EnsureCreatedAsync` em vez de migrations?

Para este projeto de estudo, `EnsureCreatedAsync` na inicialização é suficiente e elimina a necessidade de gerenciar arquivos de migration. O schema é criado automaticamente na primeira execução.

### Por que Scalar em vez de Swagger UI?

Scalar oferece uma UI mais moderna e é a recomendação do ecossistema .NET 10. Em PrecoAPI e ProdutoAPI, `app.MapOpenApi()` e `app.MapScalarApiReference()` são chamados incondicionalmente (sem checar `IsDevelopment()`) — as duas são APIs de exemplo criadas para testar o MCP, não serviços reais expostos a usuários finais, então `/scalar/v1` e `/openapi/v1.json` ficam acessíveis em qualquer ambiente, incluindo `Production`. As sondas de readiness/liveness usam o endpoint dedicado `/health`, independente do Scalar.

### Por que `imagePullPolicy: Never`?

As imagens são buildadas localmente e importadas diretamente no cache do k3d com `k3d image import`. Isso elimina a necessidade de um registry externo para desenvolvimento local.

### Por que o McpServer usa `InClusterConfig`?

O McpServer roda dentro do cluster. `InClusterConfig` usa automaticamente o ServiceAccount montado no pod (`/var/run/secrets/kubernetes.io/serviceaccount/`). O ServiceAccount `mcp-reader` tem apenas permissões de leitura (get, list) nos recursos necessários.

### Por que RunInWSL usa `bash -lc` (login shell)?

`bash -c` não carrega `~/.profile`, então ferramentas instaladas em `~/.local/bin` (k3d, kubectl) não seriam encontradas. `bash -lc` carrega o profile e garante o PATH correto.

### Por que o nome da função helper é `RunInWSL` e não `Wsl`?

PowerShell resolve nomes de função case-insensitivamente. Uma função chamada `Wsl` interceptaria chamadas a `wsl` dentro de scripts filhos (como `wsl-check.ps1` que chama `wsl -- bash -c ...`), causando recursão infinita ou argumentos corrompidos. O nome `RunInWSL` é suficientemente distinto do executável `wsl.exe`.

### Captura de Body como feature flag

A captura de body HTTP nos spans OTLP está desabilitada por padrão para evitar logging acidental de dados sensíveis. É ativada explicitamente via `-CaptureBody` no script de deploy ou via `Otel:CaptureBody: true` no ConfigMap.

---

## Portas e Acessos

| Serviço | Port-forward local | Acesso |
|---|---|---|
| PrecoAPI | `localhost:5001` | `http://localhost:5001/scalar/v1` |
| ProdutoAPI | `localhost:5002` | `http://localhost:5002/scalar/v1` |
| McpServer | `localhost:4000` | `http://localhost:4000/health` |
| **Dashboard** | `localhost:4000` | **`http://localhost:4000/dashboard`** |
| Jaeger UI | `localhost:16686` | `http://localhost:16686` |
| Prometheus | `localhost:9090` | `http://localhost:9090` |
| Grafana | `localhost:3000` | `http://localhost:3000` (credencial no Secret local) |

Via Ingress (requer entrada no `/etc/hosts` ou `C:\Windows\System32\drivers\etc\hosts`):
```
127.0.0.1  precoapi.local produtoapi.local mcpserver.local
```

---

## Configuração do Ambiente

### Pré-requisitos

- Windows com WSL2 (Ubuntu)
- No WSL: `k3d`, `kubectl`, `docker` em `~/.local/bin` ou `/usr/local/bin`
- K3s 1.36.1+ para enforcement validado de NetworkPolicy (novos clusters são pinados pelo script)
- Docker daemon rodando no WSL
- .NET 10 SDK (para desenvolvimento local)

### Subir o ambiente

```powershell
# Na raiz do repositório
.\infra\scripts\ps\up-k8s.ps1 -Build
```

### Configurar MCP no VS Code

Adicionar ao `.vscode/mcp.json`:
```json
{
  "servers": {
    "mcp-apis": {
      "type": "http",
      "url": "http://localhost:4000/"
    }
  }
}
```

Inicie os port-forwards antes de usar o MCP:
```powershell
.\infra\scripts\ps\port-forward.ps1
```

### Derrubar o ambiente

```powershell
.\infra\scripts\ps\down-k8s.ps1
```

---

## Desenvolvimento com Tilt

O [Tilt](https://tilt.dev) é a forma recomendada para desenvolvimento ativo — ele automatiza o ciclo build → deploy → port-forward e faz hot reload ao detectar mudanças nos arquivos fonte.

```bash
tilt up    # sobe tudo e abre a UI em http://localhost:10350
tilt down  # encerra e remove os recursos do cluster
```

| Serviço | Port-forward (Tilt) |
|---|---|
| PrecoAPI | `http://localhost:8081` |
| ProdutoAPI | `http://localhost:8082` |
| McpServer | `http://localhost:4000` |
| **Dashboard** | **`http://localhost:4000/dashboard`** |
| Grafana | `http://localhost:3000` |
| Prometheus | `http://localhost:9090` |
| Jaeger UI | `http://localhost:16686` |

📄 Documentação completa: [`doc/tilt.md`](doc/tilt.md)
