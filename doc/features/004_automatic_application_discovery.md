# Feature 004 — Descoberta Automática de Aplicações + Switch de Indexação MCP

## 🎯 Visão Geral

O MCP Server agora reconhece **automaticamente todas as aplicações do cluster** — sem precisar declará-las na configuração nem rotular Services — correlacionando três sinais independentes:

| Sinal | Fonte | O que prova |
|---|---|---|
| **Deployment** | Kubernetes API (`Deployments`, cluster-wide) | A aplicação tem um workload declarado |
| **Rede** | Kubernetes API (`Services` + `Endpoints`) | A aplicação é alcançável via rede (endpoints prontos) |
| **OTel** | Jaeger `GET /api/services` | A aplicação emite traces para o coletor |

O resultado é um **catálogo de aplicações** (`ApplicationCatalog`) exibido no dashboard, onde cada aplicação tem um **switch para habilitar/desabilitar a indexação MCP**. Aplicações desabilitadas ficam invisíveis para as ferramentas MCP — o LLM não consegue buscar dados sobre elas.

```
DiscoveryOrchestrator (scan no startup + a cada 60s + sob demanda)
  ├─► Kubernetes: Deployments + Services + Endpoints (todos os namespaces)
  ├─► Jaeger:     /api/services (quem emite traces)
  ├─► Config:     seção Services (compat com feature 003)
  │
  ├─► Correlação de identidade (matching estrutural + normalização de nomes)
  ├─► Validação OpenAPI (ServiceValidator, feature 003)
  ├─► Estado do toggle (ConfigMap mcpserver-state)
  │
  └─► ApplicationCatalog.ReplaceSnapshot()  ← fonte de verdade
         ├─► ServiceRegistry (view: enabled && OpenAPI válida) → tools de spec
         ├─► ToolGuard → tools de traces/health/métricas
         └─► Dashboard (GET /api/dashboard/applications + switch)
```

---

## 🧩 Dois conceitos ortogonais

| Conceito | O que controla | Quem define |
|---|---|---|
| **`enabled`** (toggle) | Se as tools MCP podem buscar **qualquer** dado da aplicação | Operador, via switch no dashboard |
| **`openApi.validated`** | Se as tools baseadas em spec (`get_openapi`, `explain_api`, `find_data_origin`) funcionam | Validação automática (feature 003) |

Uma aplicação **sem OpenAPI** (ex.: um worker que só emite traces) pode ser habilitada normalmente — `trace_route` e `get_health` funcionam; só as tools de spec respondem que não há spec válida.

### Estado padrão: opt-in

Aplicações recém-descobertas nascem **desabilitadas** — o operador habilita explicitamente no dashboard. Exceções que nascem **habilitadas** (preservam o comportamento anterior):

1. Declaradas na seção `Services` da config (ex.: `Services__precoapi`);
2. Services com label `mcp-apis/indexed: "true"` (feature 003).

### Precedência do estado

```
label mcp-apis/indexed=false  →  HARD-OFF (switch travado no dashboard)
       └─ senão → escolha do usuário (ConfigMap mcpserver-state)
                     └─ senão → default por origem (Config/label=true → on; auto-descoberta → off)
```

---

## 🔗 Correlação de identidade

Deployments usam kebab-case (`preco-api`), OTel service names usam PascalCase (`PrecoAPI`). O `NameNormalizer` reduz ambos à mesma chave:

```
normalize(s) = lowercase(s) sem caracteres fora de [a-z0-9]
"PrecoAPI" → "precoapi"    "preco-api" → "precoapi"
```

Passos do matching (em `DiscoveryOrchestrator`):

| # | Etapa | Regra |
|---|---|---|
| 1 | Service ↔ Deployment | **Matching estrutural**: `spec.selector` do Service é subconjunto dos labels do pod template do Deployment (mesma regra do K8s), no mesmo namespace |
| 2 | Nome canônico | `normalize(deployment.name)` (senão `normalize(service.name)`). Colisão entre namespaces → sufixo `-{namespace}` + warning |
| 3 | OTel ↔ grupo | `normalize(jaegerName) == chave canônica`. **Override**: annotation `mcp-apis/otel-service-name: "<NomeExato>"` no Service tem precedência |
| 4 | OTel sem match | Vira aplicação **OTel-only** (`baseUrl: null`, aparece como "detectada apenas por traces") |
| 5 | Config ↔ grupo | `normalize(alias)`; sem match vira aplicação `config`-only (compat modo `Config`) |

> 💡 Se o `service.name` OTel não é reconciliável por normalização (ex.: Deployment `payments` emitindo `BillingAPI`), anote o Service:
> ```yaml
> metadata:
>   annotations:
>     mcp-apis/otel-service-name: "BillingAPI"
> ```

### Resolução de Base URL

1. Annotation `mcp-apis/base-url` no Service;
2. FQDN gerado: `http://<service>.<namespace>.svc.cluster.local`;
3. Valor da config `Services__<alias>` (fallback);
4. OTel-only → sem base URL (não indexável por spec, mas rastreável).

---

## ⚙️ Modos de descoberta

`Discovery__Mode` agora aceita **`Auto`** (novo padrão):

| Modo | Fontes |
|---|---|
| `Auto` ⭐ | Deployments + Services/Endpoints (cluster-wide) + Jaeger + Config |
| `Config` | Somente seção `Services` (comportamento da feature 003) |
| `Kubernetes` | Somente Services com label `mcp-apis/indexed=true` |
| `Both` | Config + label — K8s sobrescreve Config |

Os modos legados continuam funcionando sem alterações (sem breaking change).

### Ciclo de vida do scan

- **Startup**: scan bloqueante antes de aceitar tráfego (catálogo já populado para clientes MCP).
- **Re-scan**: a cada `Discovery__RescanSeconds` (padrão 60s; `0` desliga) ou via `POST /api/dashboard/discovery/rescan`.
- **Falha parcial**: cada fonte roda em try/catch próprio — se o Jaeger cair, a descoberta K8s continua.
- **App removida do cluster**: fica marcada `missing` no dashboard; esquecida após `Discovery__ForgetAfterMinutes` (padrão 60).
- **Validação OpenAPI**: sucessos são reaproveitados por `Discovery__RevalidateSeconds` (padrão 300s); falhas são re-tentadas a cada scan, com limite de 8 validações concorrentes.

---

## 🔒 Semântica do toggle nas ferramentas MCP

| Tool | Comportamento com aplicação desabilitada |
|---|---|
| `get_openapi` | Mensagem "disabled for MCP indexing" com hint do dashboard; lista `Available` só com habilitadas |
| `explain_api` | Erro JSON estruturado (`ToolGuard`) |
| `find_data_origin` | Erro JSON estruturado (`ToolGuard`) |
| `get_health` | Bloqueada. Infra fora do catálogo (jaeger, prometheus...) continua permitida (**fail-open**) |
| `trace_route` | Bloqueada. Para habilitadas, resolve o alias para o nome OTel cru (Jaeger é case-sensitive) |
| `find_dependencies` | Arestas que tocam a aplicação somem do grafo; campo `disabledApplications` informa o LLM |
| `list_services` | Services/deployments/pods da aplicação omitidos; campo `disabledApplications` informa o LLM |
| `query_metrics` | Recusa **best-effort** se a query menciona o nome da aplicação. ⚠️ PromQL é livre (regex, relabels) — o gate não é hermético; limitação registrada |
| `list_discovered_applications` 🆕 | Sempre mostra o inventário completo (inclusive desabilitadas e motivos de não-indexabilidade) |

> Os endpoints do dashboard (`/api/dashboard/*`) **não** aplicam o gate — o dashboard é a UI de administração e enxerga tudo. O gate vale apenas para o plano MCP.

---

## 💾 Persistência do toggle

As escolhas do operador sobrevivem a restarts do pod via ConfigMap **`mcpserver-state`** (chave `indexing-overrides`, JSON `{"app": true|false}` — só escolhas explícitas; defaults não são gravados):

```bash
kubectl -n mcp-apis get cm mcpserver-state -o jsonpath='{.data.indexing-overrides}'
# {"precoapi":false}
```

- Escrita com retry em conflito 409 (releitura de `resourceVersion`) + serialização por semáforo.
- O ConfigMap é **pré-criado** pelos manifests (`state-configmap.yaml`) porque o RBAC restringe `update/patch` a esse objeto via `resourceNames` — e `resourceNames` não cobre o verb `create`.
- Se o ConfigMap não existir ou a escrita for negada, o servidor degrada para memória (o `PUT` responde `persisted: false`).

## 🛡️ RBAC

`infra/k8s/aplicacao/mcpserver/rbac.yaml`:

| Escopo | Recursos | Verbs | Para quê |
|---|---|---|---|
| **ClusterRole** `mcp-reader-cluster-role` | pods, services, endpoints, deployments | get, list | Descoberta cluster-wide |
| **Role** `mcp-reader-role` (ns mcp-apis) | configmaps | get, list | Ler estado |
| **Role** (com `resourceNames: [mcpserver-state]`) | configmaps | update, patch | Persistir o toggle (somente esse objeto) |

O ServiceAccount `mcp-reader` **não** tem permissão de escrita em Services, Deployments ou qualquer outro recurso.

---

## 🌐 Endpoints REST (dashboard)

| Rota | Método | Descrição |
|---|---|---|
| `/api/dashboard/applications` | GET | Inventário completo: fontes, namespace, base URL, endpoints prontos, validação OpenAPI (com motivos), enabled/locked, firstSeen/lastSeen/missing, health |
| `/api/dashboard/applications/{name}/indexing` | PUT | Body `{"enabled": bool}` → `200 {name, enabled, persisted}` \| `404` desconhecida \| `409` travada por label |
| `/api/dashboard/discovery/rescan` | POST | `202 {status: "scan-requested"}` — dispara scan no background service |

## 🖥️ Dashboard

O painel **"Aplicações descobertas"** substitui o antigo grid de serviços:

- **Switch** por aplicação (optimistic update; travado com tooltip quando `mcp-apis/indexed=false`);
- **Badges de fonte**: `Deploy` / `Rede` / `OTel` / `Config` + namespace;
- **Badge OpenAPI**: verde com o path quando validada; "Não indexável" com tooltip dos motivos quando não;
- Badge **"Não vista há Xmin"** para aplicações que sumiram do cluster;
- Botão **Re-scan** com timestamp do último scan;
- Card com opacidade reduzida quando desabilitada.

---

## 📋 Referência de configuração

| Variável | Padrão | Descrição |
|---|---|---|
| `Discovery__Mode` | `Auto` | `Auto` \| `Config` \| `Kubernetes` \| `Both` |
| `Discovery__RescanSeconds` | `60` | Intervalo do re-scan periódico (`0` = só startup + manual) |
| `Discovery__RevalidateSeconds` | `300` | Reuso de validações OpenAPI bem-sucedidas |
| `Discovery__ForgetAfterMinutes` | `60` | Tempo até esquecer aplicações não vistas |
| `Discovery__StateConfigMap` | `mcpserver-state` | ConfigMap que persiste o toggle |
| `Discovery__ExcludeNamespaces__N` | `kube-system`, `kube-public`, `kube-node-lease` | Namespaces nunca escaneados |
| `Discovery__ExcludeApps__N` | `mcpserver`, `jaeger`, `prometheus`, `grafana`, `loki`, `promtail`, `postgres-*`, `kubernetes` | Infra fora do catálogo (fail-open nas tools) |
| `Discovery__ExcludeOtelServices__N` | `jaeger-query`, `McpServer` | Nomes OTel ignorados |
| `Discovery__KubernetesLabel` | `mcp-apis/indexed` | Label de opt-in/hard-off (feature 003) |

### Annotations reconhecidas (no Service)

| Annotation | Efeito |
|---|---|
| `mcp-apis/base-url` | URL base explícita (precedência sobre FQDN) |
| `mcp-apis/otel-service-name` | Mapeia o Service para um `service.name` OTel não-reconciliável por normalização |

---

## ⚠️ Limitações conhecidas

1. **`query_metrics` é fail-open por natureza** — PromQL arbitrário pode alcançar métricas de apps desabilitadas via regex/relabels. O gate por menção de nome é best-effort.
2. **Jaeger retém nomes** enquanto houver traces na retenção — aplicações removidas podem persistir como OTel-only `missing` por um tempo.
3. **Sem autenticação** nos endpoints de mutação do dashboard (projeto de estudo).
4. Colisões de nomes normalizados no mesmo namespace (`preco-api` + `precoapi`) são fundidas com warning no log.
