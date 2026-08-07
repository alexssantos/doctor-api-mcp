# Especificação de implementação incremental — MCP Observability Intelligence

**Status:** plano de implementação; nenhuma mudança de código incluída  
**Data da análise:** 2026-08-07  
**Especificação de origem:** [MCP Observability Intelligence para AKS](mcp-observability-intelligence-especificacao.md)  
**Baseline analisada:** branch <code>master</code>, commit <code>d667d75</code>, incluindo o working tree atual  
**Escopo:** comparar a proposta com o projeto existente, registrar aderências, lacunas e conflitos e definir o plano de evolução

---

## 1. Resultado da análise

O projeto atual já entrega uma fundação útil de observabilidade: servidor MCP HTTP, descoberta automática, catálogo de aplicações, validação OpenAPI, acesso a Kubernetes, Prometheus e Jaeger, dashboard e instrumentação OpenTelemetry. Essa base deve ser **evoluída, não reimplementada**.

Entretanto, o sistema atual ainda funciona principalmente como um **gateway de descoberta e consulta aos backends**. A proposta exige uma camada adicional de contratos normalizados e engines determinísticos que transforme sinais em indicadores, evidências, anomalias, correlações e diagnósticos.

Em termos das fases da especificação de origem:

| Marco proposto | Situação atual | Conclusão |
|---|---|---|
| Fundação MCP | Parcialmente atendido | Transporte, DI, providers básicos e OTel existem; faltam autenticação, contratos vNext, limites e testes |
| Catálogo e especificação | Parcialmente atendido | Descoberta e catálogo são fortes; faltam metadados e uma tool consolidada de especificação |
| Health MVP | Não atendido semanticamente | Existe pod health, mas não Health Engine, métricas RED, score, findings ou janela |
| Dependências e traces | Parcialmente atendido | Há traces e grafo bruto do Jaeger, sem engine, profundidade, causalidade ou blast radius |
| Anomalias | Ausente | Não há baseline nem detecção de mudança |
| Timeline e correlação | Ausente | Não há modelo temporal ou eventos de deploy |
| Logs e enriquecimento | Infraestrutura parcial | Loki/Promtail existem, mas o MCP não os consulta |
| RCA | Ausente | Não há geração, ranking ou evidências de hipóteses |
| Recommendations e visão SRE | Ausente | Não há recomendações nem resumo sistêmico |

O primeiro marco de negócio continua sendo a saúde consolidada do serviço, mas ele deve ser precedido por uma curta fase de convergência arquitetural e segurança. Construir o Health Engine diretamente sobre os collectors atuais perpetuaria contratos acoplados a Prometheus e Jaeger e respostas sem versionamento.

---

## 2. Método e legenda

A análise considerou código C#, manifests Kubernetes, configuração, documentação das features anteriores e scripts de validação. O código foi considerado a fonte de verdade quando a documentação descreve uma intenção diferente do comportamento implementado.

Foi executado <code>dotnet build src/mcp-apis.slnx --no-restore</code> com sucesso: zero erros e doze warnings. Não foi executada validação contra um cluster AKS ou k3d ativo. Não existem projetos automatizados de teste na solução atual.

Legenda usada neste documento:

| Classificação | Significado |
|---|---|
| **Atendido** | A capacidade existe e pode ser reaproveitada sem mudança estrutural relevante |
| **Alinhado, mas parcial** | Segue o mesmo objetivo, porém o contrato, a cobertura ou a qualidade precisam evoluir |
| **Ausente** | Não há implementação equivalente no projeto |
| **Conflito** | O comportamento atual contradiz uma decisão explícita da proposta e exige resolução |

---

## 3. Arquitetura efetivamente implementada

O fluxo predominante atual é:

~~~text
Cliente MCP ou dashboard
          |
          v
Tools MCP e endpoints REST
          |
          v
Collectors específicos de backend
          |
          +--> Kubernetes API
          +--> Prometheus
          +--> Jaeger
          +--> OpenAPI das aplicações
~~~

As nove tools são registradas diretamente no bootstrap do servidor em [Program.cs](../../src/Services/McpServer/Program.cs#L57). Os handlers chamam collectors e fazem projeções ad hoc; não existe uma camada de engines entre a interface MCP e os backends.

### 3.1 Capacidades já presentes

- Servidor ASP.NET Core/.NET 10 com transporte MCP HTTP.
- Catálogo thread-safe com snapshot atômico e índice de aliases, em [ApplicationCatalog.cs](../../src/Services/McpServer/Services/ApplicationCatalog.cs#L10).
- Descoberta automática que correlaciona Deployments, Services, Endpoints, nomes OTel do Jaeger e configuração estática, em [DiscoveryOrchestrator.cs](../../src/Services/McpServer/Services/DiscoveryOrchestrator.cs#L81).
- Re-scan periódico, retenção temporária de aplicações não vistas e cache de validações OpenAPI bem-sucedidas.
- Clientes HTTP tipados com timeout de dez segundos, em [Program.cs](../../src/Services/McpServer/Program.cs#L18).
- Collectors para Kubernetes, Prometheus, Jaeger e OpenAPI.
- Instrumentação genérica do próprio MCP com traces OTLP e métricas Prometheus, em [ObservabilityExtensions.cs](../../src/BuildingBlocks/Observability/ObservabilityExtensions.cs#L42).
- Prometheus, Jaeger, Loki e Promtail provisionados no ambiente de demonstração.
- Falha parcial durante a descoberta e fallback em memória para a persistência do toggle.

### 3.2 Tools atuais e destino recomendado

| Tool atual | Comportamento real | Relação com a proposta | Destino |
|---|---|---|---|
| <code>list_services</code> | Lista Services, Deployments e Pods do namespace configurado | Apoia catálogo e visão sistêmica | Manter como compatibilidade; migrar para catálogo/Kubernetes provider |
| <code>list_discovered_applications</code> | Expõe inventário correlacionado, inclusive desabilitados | Base forte do Service Catalog | Manter como tool administrativa protegida |
| <code>get_openapi</code> | Retorna o documento OpenAPI bruto | Parte de <code>service.getSpec</code> | Manter como opção legada/admin; não usar como resposta padrão |
| <code>explain_api</code> | Combina rotas OpenAPI e médias de duração de traces | Mesmo objetivo geral de <code>service.getSpec</code> | Reimplementar sobre providers normalizados |
| <code>get_health</code> | Retorna readiness, restarts e estados de Pods | Apenas o componente Kubernetes de <code>service.getHealth</code> | Preservar como pod health legado; não reutilizar o nome para nova semântica |
| <code>trace_route</code> | Agrupa spans recentes por trace | Base do Trace Adapter | Manter, impondo janela, teto e contrato normalizado |
| <code>find_dependencies</code> | Retorna o grafo global bruto do Jaeger | Parte de <code>service.getDependencies</code> | Reimplementar sobre Dependency Engine |
| <code>find_data_origin</code> | Combina rota, traces e Pods | Capacidade complementar útil a dependências/RCA | Manter após corrigir ordenação causal |
| <code>query_metrics</code> | Executa PromQL arbitrário | Conflito direto com a proposta | Retirar da superfície pública por padrão |

Evidências principais: [registro das tools](../../src/Services/McpServer/Program.cs#L68), [health atual](../../src/Services/McpServer/Tools/GetHealthTool.cs#L12), [dependências atuais](../../src/Services/McpServer/Tools/FindDependenciesTool.cs#L12) e [PromQL livre](../../src/Services/McpServer/Tools/QueryMetricsTool.cs#L11).

---

## 4. Matriz de aderência — coleta e adapters

| Componente proposto | Situação | Implementação atual | Diferença e ação planejada |
|---|---|---|---|
| Service Catalog | **Alinhado, mas parcial** | <code>DiscoveredApplication</code> guarda nome, namespace, Deployment, Service, nome OTel, URL, readiness de endpoint, OpenAPI e estado de indexação | Enriquecer com labels/annotations selecionadas, versão, imagens/digests, réplicas, owner, endpoints resumidos, IDs de métricas/traces e cobertura de sinais |
| Normalização de identidade | **Alinhado, mas parcial** | <code>NameNormalizer</code> e índice de aliases correlacionam diferentes grafias | Fazer namespace parte obrigatória da identidade interna; detectar ambiguidade em vez de depender de sufixos; manter aliases por fonte |
| Metrics Adapter | **Alinhado, mas parcial** | <code>IPrometheusCollector</code> oferece query instantânea, range e targets | Criar <code>IMetricsProvider</code> tipado; esconder PromQL; normalizar unidades, labels, timestamps e disponibilidade |
| Trace Adapter | **Alinhado, mas parcial** | <code>IJaegerCollector</code> lista serviços, traces, dependências e spans | Criar <code>ITraceProvider</code>; incluir timestamp, spanStatus, parent/references, erros, duração por dependência e janela parametrizada |
| Logs Adapter | **Ausente no MCP** | Loki/Promtail existem apenas na infraestrutura | Criar <code>ILogProvider</code>, provider Loki, agrupamento/fingerprint, amostragem e redaction |
| Kubernetes Adapter | **Alinhado, mas parcial** | Lista Pods, Deployments, Services, Endpoints; expõe readiness, restarts e estado de containers | Adicionar seletores reais, ReplicaSets, HPA, Events, requests/limits, OOMKilled, Pending/CrashLoop e, quando autorizado, Nodes |
| Application Specification Adapter | **Alinhado, mas parcial** | Valida/baixa OpenAPI e extrai rota, método, summary e operation ID | Normalizar descrição, versão, contratos, endpoints e dependências declaradas; combinar com metadados do catálogo |
| Deployment/Event Adapter | **Ausente** | Não existe histórico de deploy, rollout, mudança de imagem ou integração CI/CD/GitOps | Implementar primeiro Kubernetes Events e snapshots de versão; adicionar provider CI/CD/GitOps posteriormente |

### 4.1 Service Catalog: o que deve ser preservado

O modelo atual já resolve uma parte relevante da proposta. O contrato está em [IApplicationCatalog.cs](../../src/Services/McpServer/Services/Contracts/IApplicationCatalog.cs#L30) e a instalação de aliases em [ApplicationCatalog.cs](../../src/Services/McpServer/Services/ApplicationCatalog.cs#L112).

Ele deve ser enriquecido com:

- identidade interna composta por <code>namespace + serviceName</code>;
- seletores Kubernetes reais usados para localizar Pods, sem presumir <code>app=deploymentName</code>;
- imagem, digest, versão, réplicas desejadas/prontas e revisão do Deployment;
- labels e annotations permitidas por configuração;
- owner/time e descrição;
- identificadores específicos por provider;
- matriz de capacidades por serviço: Kubernetes, métricas, traces, logs, OpenAPI e eventos;
- <code>observedAt</code>, freshness e motivo de indisponibilidade de cada capacidade.

Essa matriz é necessária porque descoberta não significa cobertura de telemetria. O Prometheus atual faz scrape estático apenas de <code>precoapi</code>, <code>produtoapi</code> e <code>mcpserver</code>, conforme [configmap do Prometheus](../../infra/k8s/observabilidade/prometheus/configmap.yaml#L12). O Promtail coleta somente caminhos do namespace <code>mcp-apis</code>, conforme [configuração do Promtail](../../infra/k8s/observabilidade/promtail/configmap.yaml#L17). Uma aplicação pode, portanto, ser descoberta sem possuir métricas ou logs consultáveis.

### 4.2 Correções prévias no Kubernetes health

O health atual não pode ser promovido diretamente a Health Engine:

- a consulta monta <code>app=&lt;nome&gt;</code> em vez de reutilizar os seletores reais do workload, em [KubernetesService.cs](../../src/Services/McpServer/Services/KubernetesService.cs#L56);
- <code>AllReady</code> é verdadeiro quando a lista de Pods está vazia, pelo comportamento de <code>All(...)</code> sobre coleção vazia, em [KubernetesService.cs](../../src/Services/McpServer/Services/KubernetesService.cs#L62);
- não há distinção entre indisponível, desconhecido, degradado e saudável;
- não há Events, HPA, requests/limits ou motivo estruturado de OOMKilled/CrashLoop.

Para zero Pods ou falha de coleta, o provider deverá marcar a fonte como <code>unavailable</code> e o Health Engine deverá concluir <code>unknown</code>; jamais deverá inferir saúde positiva sem evidência.

### 4.3 Correções prévias em traces

O parser atual conserva trace ID, span ID, operação, duração, serviço e tags, mas descarta timestamp, span status e relações parent/child, conforme [JaegerService.cs](../../src/Services/McpServer/Services/JaegerService.cs#L49). Consequentemente, <code>find_data_origin</code> ordena spans por duração como se isso representasse a cadeia de chamadas, em [FindDataOriginTool.cs](../../src/Services/McpServer/Tools/FindDataOriginTool.cs#L56).

Antes de usar traces em correlação ou RCA, o novo modelo deve preservar causalidade, tempo absoluto, eventos/exceções e status semântico.

---

## 5. Matriz de aderência — engines de inteligência

| Engine proposto | Situação atual | Base reaproveitável | Entrega necessária |
|---|---|---|---|
| Health Engine | **Ausente** | Kubernetes pod health e Prometheus range query | Compor RED, recursos e estabilidade; calcular score, status, findings, cobertura e evidências |
| Dependency Engine | **Ausente como engine** | Endpoint global de dependências e spans do Jaeger | Grafo normalizado por serviço, inbound/outbound, depth, ciclos, criticidade e blast radius |
| Anomaly Engine | **Ausente** | Query range do Prometheus | Comparação de janelas, baseline 24h/7d, EWMA/robust Z-score e severidade |
| Baseline Store | **Ausente** | Histórico consultável no Prometheus | Usar backend de métricas no MVP; materializar somente se custo/latência exigir |
| Correlation Engine | **Ausente** | Timestamps futuros de métricas, traces, logs e Events | Modelo único de evento, ordenação, deduplicação e correlação temporal/dependência |
| RCA Engine | **Ausente** | Health, grafo, anomalias e timeline futuros | Gerar hipóteses, pontuar evidências, confiança, limitações e serviços afetados |
| Recommendation Engine | **Ausente** | Findings e hipóteses futuras | Regras versionadas que apenas recomendam; nenhuma execução automática |

Não foram localizados engines, contratos de evidência ou registradores equivalentes no bootstrap atual. As transformações existentes estão dentro das próprias tools.

---

## 6. Matriz de aderência — tools e contratos públicos

| Capacidade proposta | Situação | Correspondência atual | Plano |
|---|---|---|---|
| <code>service.getSpec</code> | **Parcial** | Catálogo + <code>get_openapi</code> + <code>explain_api</code> | Criar resposta consolidada e limitada; OpenAPI bruto fica opcional |
| <code>service.getHealth</code> | **Parcial somente em Kubernetes** | <code>get_health</code> | Criar nova tool vNext sobre Health Engine; manter a legada como pod health |
| <code>service.getDependencies</code> | **Parcial** | <code>find_dependencies</code> | Aceitar serviço/namespace/depth e retornar grafo normalizado |
| <code>service.detectAnomalies</code> | **Ausente** | Nenhuma | Implementar após Health e range metrics |
| <code>service.getIncidentTimeline</code> | **Ausente** | Nenhuma | Implementar após eventos, anomalias e traces normalizados |
| <code>service.findRootCause</code> | **Ausente** | Nenhuma | Implementar somente após Health, Dependency, Anomaly e Correlation |
| <code>service.getScore</code> | **Ausente** | Nenhuma | Expor projeção do mesmo cálculo do Health Engine, sem lógica duplicada |
| <code>system.getHealthSummary</code> | **Ausente** | Dashboard mostra contagens simples | Implementar depois que health por serviço estiver estável e cacheado |

### 6.1 Convenção de nomes e compatibilidade

A especificação usa nomes conceituais com ponto, enquanto o projeto usa <code>snake_case</code>. A troca direta quebraria clientes e o nome <code>get_health</code> já possui uma semântica incompatível.

Decisão recomendada:

| Nome conceitual | Nome físico vNext recomendado | Compatibilidade |
|---|---|---|
| <code>service.getSpec</code> | <code>service_get_spec</code> | Manter <code>get_openapi</code> e <code>explain_api</code> durante depreciação |
| <code>service.getHealth</code> | <code>service_get_health</code> | Manter <code>get_health</code> explicitamente como pod health |
| <code>service.getDependencies</code> | <code>service_get_dependencies</code> | Manter <code>find_dependencies</code> durante depreciação |
| <code>service.detectAnomalies</code> | <code>service_detect_anomalies</code> | Nova |
| <code>service.getIncidentTimeline</code> | <code>service_get_incident_timeline</code> | Nova |
| <code>service.findRootCause</code> | <code>service_find_root_cause</code> | Nova |
| <code>service.getScore</code> | <code>service_get_score</code> | Nova |
| <code>system.getHealthSummary</code> | <code>system_get_health_summary</code> | Nova |

Antes da implementação, uma prova de compatibilidade deverá validar os nomes em Claude Code, Copilot CLI, Cursor e demais clientes suportados. Se todos aceitarem ponto de forma consistente, a ADR de naming poderá optar pelos nomes literais da proposta. Não devem existir simultaneamente aliases com ponto e underscore sem uma política de depreciação, pois ferramentas duplicadas confundem o cliente de IA.

### 6.2 Contrato comum vNext

As tools atuais retornam strings, objetos anônimos, JSON bruto de backend e erros em formatos diferentes. A proposta exige contratos estáveis como ServiceIdentity, TimeWindow, Finding e Evidence.

Toda resposta vNext deverá possuir um envelope equivalente a:

~~~json
{
  "schemaVersion": "1.0",
  "generatedAt": "2026-08-07T15:00:00Z",
  "executionStatus": "complete",
  "service": {
    "serviceName": "checkout-api",
    "namespace": "ecommerce"
  },
  "window": {
    "from": "2026-08-07T14:30:00Z",
    "to": "2026-08-07T15:00:00Z",
    "duration": "30m"
  },
  "data": {},
  "sources": [
    {
      "name": "metrics",
      "availability": "available",
      "observedAt": "2026-08-07T14:59:45Z"
    }
  ],
  "warnings": [],
  "evidence": []
}
~~~

Regras:

- <code>executionStatus</code> deve ser <code>complete</code>, <code>partial</code> ou <code>unavailable</code>;
- fonte indisponível nunca deve ser representada como valor zero;
- toda medida deve indicar unidade e timestamp;
- ratios usam intervalo de zero a um; durações internas usam unidade explícita;
- erros de entrada usam códigos estáveis, por exemplo <code>unknown_service</code>, <code>ambiguous_service</code>, <code>service_disabled</code>, <code>invalid_window</code> e <code>source_unavailable</code>;
- <code>serviceName</code> sem namespace só é aceito quando o resultado for inequívoco;
- toda operação assíncrona propaga <code>CancellationToken</code>;
- DTOs públicos não expõem <code>JsonElement</code>, PromQL, LogQL ou modelos do SDK Kubernetes.

Os estados pertencem a eixos diferentes e não devem reutilizar o mesmo campo:

| Eixo | Campo | Valores iniciais |
|---|---|---|
| Execução da tool | <code>executionStatus</code> | complete, partial, unavailable |
| Disponibilidade de cada fonte | <code>availability</code> | available, stale, unavailable |
| Saúde do serviço | <code>healthStatus</code> | healthy, degraded, critical, unknown |
| Conclusão analítica | <code>analysisConclusion</code> | detected, not_detected, inconclusive |

Um relatório pode, por exemplo, ter execução <code>partial</code>, metrics <code>available</code>, logs <code>unavailable</code>, saúde <code>degraded</code> e conclusão de RCA <code>inconclusive</code>.

### 6.3 Contratos internos mínimos

| Contrato | Campos mínimos |
|---|---|
| <code>ServiceIdentity</code> | serviceName, namespace, deploymentName, kubernetesServiceName, otelServiceName, metricsId, aliases |
| <code>TimeWindow</code> | from, to, duration, comparison windows, timezone UTC |
| <code>ProviderResult&lt;T&gt;</code> | availability, value, observedAt, freshness, warnings, elapsed |
| <code>MetricSample</code> | metric, service, timestamp, value, unit, aggregation |
| <code>Finding</code> | type, severity, service, message, startedAt, evidenceIds |
| <code>Evidence</code> | id, source, signal, value, baseline, unit, timestamp, query descriptor seguro |
| <code>DependencyEdge</code> | source, target, type, observedAt, callCount, errorRate, latency, evidence |
| <code>IncidentEvent</code> | timestamp, type, service, severity, source, summary, evidenceIds |
| <code>Recommendation</code> | priority, action, reason, evidenceIds, executable=false |

---

## 7. Conflitos que precisam de decisão

### CON-01 — PromQL arbitrário

**Proposta:** impedir queries arbitrárias fornecidas pelo LLM.  
**Atual:** <code>query_metrics</code> recebe qualquer PromQL e o dashboard também oferece query/range livres. O próprio código reconhece que o gate é best-effort, em [QueryMetricsTool.cs](../../src/Services/McpServer/Tools/QueryMetricsTool.cs#L17).

**Impacto:** vazamento entre aplicações, consultas caras, contratos acoplados ao backend e impossibilidade de garantir limites.

**Resolução recomendada:**

- desabilitar <code>query_metrics</code> na superfície MCP por padrão;
- substituir por operações tipadas e templates de query internos;
- se a consulta raw for necessária em desenvolvimento, expô-la somente como capacidade administrativa autenticada, auditada, com feature flag desligada em produção;
- aplicar teto de janela, cardinalidade, séries, amostras, timeout e tamanho da resposta.

### CON-02 — Read-only versus persistência no ConfigMap

**Proposta:** service account read-only e nenhuma alteração em ConfigMaps.  
**Atual:** o MCP grava o toggle de indexação em <code>mcpserver-state</code>, em [KubernetesIndexingStateStore.cs](../../src/Services/McpServer/Services/KubernetesIndexingStateStore.cs#L63), e o RBAC permite <code>update/patch</code> nesse objeto, em [rbac.yaml](../../infra/k8s/aplicacao/mcpserver/rbac.yaml#L34).

**Impacto:** conflito literal com o requisito, embora a escrita não altere workloads observados.

**Resolução recomendada:** registrar ADR definindo o limite como **nenhuma mutação nos recursos observados**, mantendo uma exceção explícita e cirúrgica apenas para o estado administrativo do próprio MCP. A permissão deve continuar limitada por <code>resourceNames</code>, ser auditada e não ficar disponível às tools de análise. Se a governança exigir read-only literal, mover esse estado para um control plane externo e remover <code>update/patch</code>.

### CON-03 — Namespace allowlist versus varredura cluster-wide

**Proposta:** namespace allowlist e RBAC mínimo.  
**Atual:** o modo Auto lista o cluster inteiro e aplica uma denylist de namespaces; o ClusterRole permite leitura cluster-wide, em [rbac.yaml](../../infra/k8s/aplicacao/mcpserver/rbac.yaml#L7).

**Resolução recomendada:**

- introduzir <code>Security:AllowedNamespaces</code>, obrigatório fora de Development;
- filtrar no servidor e, quando operacionalmente viável, substituir ClusterRole por Roles/Bindings nos namespaces permitidos;
- alinhar Prometheus/Promtail à mesma allowlist;
- tornar desconhecidos e infraestrutura não declarada fail-closed no plano MCP;
- manter inventário global somente em uma interface administrativa separada, se realmente necessário.

### CON-04 — Fail-open usado como controle de acesso

<code>ToolGuard</code> e <code>ApplicationCatalog.IsEnabled</code> permitem nomes desconhecidos para preservar acesso à infraestrutura, conforme [ToolGuard.cs](../../src/Services/McpServer/Tools/ToolGuard.cs#L6).

**Resolução recomendada:** autorização e escopo não podem depender do catálogo. Tools vNext devem exigir identidade resolvida, namespace permitido e aplicação habilitada. Infraestrutura consultável deve estar em allowlist explícita e em tools administrativas próprias.

### CON-05 — Mesmo nome de health, semântica diferente

O <code>get_health</code> atual significa “estado dos Pods”. A nova capacidade significa saúde consolidada com janela, RED, recursos, score, findings e evidências.

**Resolução recomendada:** criar <code>service_get_health</code> vNext e preservar <code>get_health</code> como legado durante a janela de migração. Não alterar silenciosamente o JSON da tool existente.

### CON-06 — Interfaces existem, mas vazam o backend

Os collectors atuais são interfaces, porém <code>IPrometheusCollector</code> recebe PromQL e retorna <code>JsonElement</code>; <code>IJaegerCollector</code> expõe formas específicas do Jaeger.

**Resolução recomendada:** manter esses collectors como adapters internos de infraestrutura e adicionar providers de domínio acima deles. Engines dependem apenas de <code>IMetricsProvider</code>, <code>ITraceProvider</code>, <code>ILogProvider</code>, <code>IKubernetesProvider</code>, <code>IApplicationSpecProvider</code> e <code>IDeploymentEventProvider</code>.

### CON-07 — Ausência de autenticação e autorização

O pipeline mapeia MCP, dashboard e endpoints administrativos sem autenticação ou policies, em [Program.cs](../../src/Services/McpServer/Program.cs#L92). O PUT de indexação e o POST de rescan também ficam expostos.

**Resolução recomendada:** definir autenticação adequada ao ambiente antes da primeira tool vNext; separar policies <code>ObservabilityReader</code> e <code>ObservabilityAdmin</code>; proteger dashboard, mutações, inventário completo e, se mantida, uma interface de raw query estritamente administrativa e separada do MCP. O mecanismo concreto de identidade deve ser decidido conforme o ambiente de implantação.

### CON-08 — Captura de body sem redaction

A captura está desligada por padrão, mas, quando habilitada, grava request/response body truncado sem redigir PII ou segredos, em [RequestBodyTelemetryMiddleware.cs](../../src/BuildingBlocks/Observability/RequestBodyTelemetryMiddleware.cs#L18) e [ResponseBodyTelemetryMiddleware.cs](../../src/BuildingBlocks/Observability/ResponseBodyTelemetryMiddleware.cs#L18).

**Resolução recomendada:** manter desligado por padrão; usar allowlist de content types/campos, limite antes da leitura integral, redaction central, exclusão de secrets/tokens e testes com fixtures de PII.

### CON-09 — Descoberta de aplicação versus cobertura de sinal

Auto-discovery identifica workloads em vários namespaces, enquanto Prometheus e Promtail cobrem somente alvos/paths estáticos do ambiente de demonstração.

**Resolução recomendada:** modelar cobertura explicitamente no catálogo e impedir que ausência de sinal seja interpretada como valor saudável. Adotar descoberta de scrape/logs controlada por allowlist e opt-in, ou documentar o backend corporativo que fornece cobertura equivalente.

### CON-10 — Alvo AKS versus ambiente local atual

**Proposta:** operação em AKS.  
**Atual:** o repositório é um ambiente de estudo k3d, conforme [README](../../README.md#L3), usa host <code>mcpserver.local</code> no [Ingress](../../infra/k8s/aplicacao/mcpserver/ingress.yaml#L8) e imagens locais com <code>imagePullPolicy: Never</code> no [Deployment](../../infra/k8s/aplicacao/mcpserver/deployment.yaml#L25).

O código in-cluster e os manifests básicos são portáveis, mas isso não equivale a prontidão para AKS.

**Resolução recomendada:** criar overlays/configuração por ambiente e validar no rollout:

- registry e política de pull adequados;
- DNS, TLS e autenticação;
- identidade e secrets do ambiente;
- NetworkPolicy e egress aos backends;
- endpoints de observabilidade escolhidos para AKS;
- disponibilidade, PodDisruptionBudget e estratégia de réplicas;
- comportamento de cache, catálogo e sessões MCP com múltiplas réplicas;
- RBAC/allowlist reais do cluster de destino.

---

## 8. Arquitetura-alvo incremental

~~~mermaid
flowchart TD
    A[Clientes MCP] --> B[Auth, autorização e limites]
    B --> C[Tools MCP vNext]
    C --> D[Health Engine]
    C --> E[Dependency Engine]
    C --> F[Anomaly Engine]
    C --> G[Correlation Engine]
    C --> H[RCA Engine]
    C --> I[Recommendation Engine]

    D --> J[Providers normalizados]
    E --> J
    F --> J
    G --> J
    H --> D
    H --> E
    H --> F
    H --> G
    I --> H

    K[Service Catalog e Identity Resolver] --> C
    K --> J

    J --> L[Prometheus adapter]
    J --> M[Jaeger adapter]
    J --> N[Loki adapter]
    J --> O[Kubernetes adapter]
    J --> P[OpenAPI adapter]
    J --> Q[Deployment/Event adapters]

    R[Cache, resiliência, auditoria e OTel] -. transversal .-> C
    R -. transversal .-> J
~~~

Princípios:

- tools finas: validação, autorização, chamada do engine e serialização;
- engines determinísticos: cálculo, comparação, correlação e evidência;
- adapters isolam APIs e formatos de backend;
- nenhum engine recebe PromQL, LogQL, JSON bruto ou tipos do Kubernetes SDK;
- falha de uma fonte gera resultado parcial explicável;
- linguagem natural continua responsabilidade do cliente de IA;
- recommendations nunca executam ações;
- contratos públicos são versionados e testados por snapshot/schema.

### 8.1 Organização sugerida no projeto

Não é necessário dividir a solução em muitos assemblies no primeiro incremento. Dentro do <code>McpServer</code>, criar fronteiras claras:

~~~text
Domain/
  Contracts/
  Health/
  Dependencies/
  Incidents/

Providers/
  Contracts/
  Prometheus/
  Jaeger/
  Loki/
  Kubernetes/
  OpenApi/
  DeploymentEvents/

Engines/
  Health/
  Dependencies/
  Anomalies/
  Correlation/
  Rca/
  Recommendations/

Tools/
  Legacy/
  VNext/

Infrastructure/
  Caching/
  Resilience/
  Security/
  Telemetry/
~~~

Uma separação em projetos próprios só deverá ocorrer quando houver necessidade real de reutilização, isolamento de dependências ou tempo de build/teste.

---

## 9. Especificação dos providers

### 9.1 Metrics Provider

Operações mínimas:

- request rate;
- error rate por 4xx, 429, 5xx, timeout e dependency error;
- P50, P95 e P99;
- disponibilidade;
- CPU e memória;
- restart rate e saturação quando a fonte oferecer;
- range normalizado para comparação de janelas.

As queries Prometheus devem ser templates internos versionados. Mapeamentos de nome de métrica/labels devem ser configuráveis por ambiente e por serviço, pois diferentes stacks OTel produzem convenções distintas.

Pré-requisitos de infraestrutura:

- ampliar o scrape além dos três targets estáticos ou integrar um backend corporativo;
- disponibilizar métricas de infraestrutura adequadas para CPU, memória, requests/limits e saturação;
- validar a cardinalidade e a retenção necessárias a comparações de 24h/7d.

### 9.2 Trace Provider

Operações mínimas:

- traces lentos;
- traces com erro;
- trace por ID;
- dependências observadas;
- latência/erro por dependência;
- traces em janela explícita.

Cada span normalizado deve preservar:

- trace ID e span ID;
- parent span/references;
- serviço e operação;
- início e duração;
- spanStatus;
- exceções/eventos;
- atributos HTTP, RPC, messaging e DB permitidos;
- downstream/peer;
- indicador de dados redigidos.

### 9.3 Logs Provider

Operações mínimas:

- erros por serviço;
- busca por trace ID;
- busca por Pod;
- janela ao redor de um evento;
- padrões/fingerprints de erro.

Regras obrigatórias:

- limite de linhas e bytes antes da resposta;
- agrupamento e deduplicação;
- amostras representativas;
- redaction antes de cache, logs do MCP e retorno ao cliente;
- nenhuma LogQL fornecida pelo cliente;
- distinguir “nenhum log encontrado” de “fonte indisponível”;
- propagar trace/span IDs nos logs das aplicações como pré-requisito para correlação confiável.

### 9.4 Kubernetes Provider

Deve aproveitar o collector atual e evoluir para:

- consultas por seletores reais do catálogo;
- Pods, Deployments, ReplicaSets, Services, HPA e Events;
- readiness, estado de containers, restarts, OOMKilled, Pending e CrashLoopBackOff;
- requests/limits e réplicas desejadas/prontas;
- versão, imagem e revisão;
- escopo limitado aos namespaces autorizados.

Nodes só devem ser incluídos quando indispensáveis para explicar pressão/eviction e com RBAC explicitamente aprovado.

### 9.5 Application Specification Provider

Deve combinar:

- metadados do catálogo;
- OpenAPI parseada e resumida;
- versão/imagem/revisão;
- endpoints, métodos, descrições e contratos relevantes;
- dependências declaradas;
- owner/time;
- recursos e capacidades observáveis.

O documento OpenAPI bruto não deve fazer parte do retorno padrão de <code>service_get_spec</code>. Ele poderá ser acessado por uma tool legada/admin com limite de tamanho.

### 9.6 Deployment/Event Provider

Estratégia incremental:

1. Kubernetes Events, rollout/revision, troca de imagem e snapshots periódicos;
2. Argo CD ou outro GitOps usado pelo ambiente;
3. GitHub Actions, Azure DevOps ou fonte corporativa;
4. normalização em <code>IncidentEvent</code>.

Kubernetes Events têm retenção limitada; a timeline histórica exigirá persistência própria ou uma fonte CI/CD/GitOps durável.

---

## 10. Especificação dos engines

### 10.1 Health Engine v1

Dimensões iniciais e pesos configuráveis:

| Dimensão | Peso sugerido |
|---|---:|
| Disponibilidade/readiness | 30 |
| Erros | 25 |
| Latência | 20 |
| Saturação de CPU/memória | 15 |
| Estabilidade de Pods | 10 |

Cada dimensão produz score de zero a cem e evidências. O score final considera apenas dimensões disponíveis, mas deve publicar também <code>coverage</code>, calculada pelo peso coberto. Cada serviço possui um health profile que marca dimensões obrigatórias e opcionais. Para evitar falso positivo:

- coverage abaixo do mínimo para calcular score, inicialmente 60%, produz <code>healthStatus=unknown</code>;
- ausência de qualquer dimensão obrigatória produz <code>healthStatus=unknown</code>, mesmo que exista score provisório;
- <code>healthStatus=healthy</code> exige todas as dimensões obrigatórias e coverage mínima própria, inicialmente 80%;
- zero Pods nunca produz saudável;
- fonte indisponível não equivale a zero erro ou zero latência;
- threshold por serviço/SLO tem precedência sobre defaults globais;
- baseline complementa thresholds somente após o Anomaly Engine estar disponível.

Faixas iniciais, configuráveis:

| Score | healthStatus |
|---:|---|
| 85–100 | healthy |
| 60–84 | degraded |
| 0–59 | critical |
| cobertura insuficiente | unknown |

<code>service_get_score</code> deve ser somente uma projeção do relatório produzido pelo Health Engine; não deve recalcular regras.

### 10.2 Dependency Engine

Deve mesclar:

- dependências observadas em traces;
- mapa de serviços do backend;
- dependências declaradas em spec/config;
- identidades do catálogo.

Cada aresta informa fonte, freshness, volume, erro e latência quando disponíveis. A consulta deve:

- aceitar profundidade com teto configurável;
- separar inbound e outbound;
- evitar loops em grafos cíclicos;
- sinalizar arestas declaradas mas não observadas e observadas mas não declaradas;
- calcular blast radius como impacto potencial, não como certeza;
- identificar caminho crítico com evidência de volume/latência.

### 10.3 Anomaly Engine

Ordem de implementação:

1. janela atual versus anterior;
2. mesma janela 24h atrás;
3. mesma janela sete dias atrás;
4. média móvel/EWMA;
5. robust Z-score.

Toda anomalia deve indicar valor atual, esperado, desvio, método, amostras, início estimado, severidade e evidências. ML complexo permanece fora do MVP.

### 10.4 Correlation Engine

Deve transformar sinais em uma timeline única:

- anomalias;
- deploys e mudanças de imagem/configuração;
- Kubernetes Events;
- restarts;
- traces lentos/com erro;
- padrões de logs.

A correlação inicial é determinística por proximidade temporal, identidade e relação de dependência. Eventos duplicados de fontes diferentes devem ser agrupados, preservando suas evidências.

### 10.5 RCA Engine

O engine gera e pontua hipóteses; não produz uma afirmação causal sem suporte.

Critérios de ranking:

- proximidade temporal;
- direção no grafo;
- intensidade da anomalia;
- participação em traces afetados;
- erro compartilhado;
- deploy/mudança recente;
- propagação upstream/downstream;
- evidência que contradiz a hipótese.

O retorno inclui:

- hipótese principal e alternativas;
- confidence calibrada;
- evidências favoráveis e contrárias;
- cobertura das fontes;
- serviços potencialmente afetados;
- limitações da análise.

Se a evidência mínima não for atingida, a resposta deve ser <code>inconclusive</code>, nunca uma causa inventada.

### 10.6 Recommendation Engine

Deve usar regras versionadas por tipo de finding/causa:

- endpoint/dependência a investigar;
- versão atual versus anterior;
- pool de conexões;
- timeout/retry/circuit breaker;
- CPU/memória/HPA;
- banco/cache;
- possibilidade de rollback para avaliação humana.

Toda recomendação possui <code>executable=false</code>. Nenhuma tool de escrita, rollback, restart ou scale faz parte deste plano.

---

## 11. Cache, limites e resiliência

### 11.1 Cache

Reaproveitar o snapshot atual do catálogo e adicionar cache por capacidade:

| Dado | TTL inicial | Observação |
|---|---:|---|
| Catálogo | 60 s a 5 min | O re-scan atual de 60 s pode ser mantido |
| OpenAPI parseada | 15–60 min | Invalidar por versão/imagem/ETag quando disponível |
| Kubernetes state | 15–30 s | Preferir consulta agregada, não uma chamada por app |
| Health report | 15–30 s | Chave inclui serviço, namespace, janela e versão das regras |
| Dependency graph | 1–5 min | Chave inclui janela e profundidade |
| Deploy history | 1 min | Persistência pode ser necessária para histórico |

Requisitos:

- single-flight para evitar stampede;
- <code>observedAt</code> e idade em todo valor;
- stale-while-revalidate apenas quando sinalizado como stale;
- invalidação no rescan, toggle, mudança de versão e configuração;
- métricas de hit, miss, stale e eviction.

O endpoint atual de aplicações consulta Kubernetes sequencialmente para cada app, em [ApplicationsEndpoints.cs](../../src/Services/McpServer/Api/ApplicationsEndpoints.cs#L30). O plano deve substituí-lo por snapshot/batch para eliminar o padrão N+1.

### 11.2 Limites

Definir via options validadas no startup:

- janela padrão e máxima por tool;
- máximo de traces, spans, logs, séries, amostras e dependências;
- tamanho máximo de resposta;
- timeout total da tool e por provider;
- concorrência por backend e por usuário;
- profundidade máxima do grafo;
- step mínimo de range query;
- limite de corpo antes de bufferização.

Os parâmetros atuais <code>trace_route.limit</code> e <code>minutes/step</code> do dashboard não possuem teto efetivo.

### 11.3 Fallback

O padrão de falha parcial da descoberta deve ser generalizado:

- cada provider retorna <code>ProviderResult</code> com disponibilidade própria;
- tool agrega fontes concluídas e publica warnings das indisponíveis;
- cache stale pode ser usado com idade explícita;
- retry com backoff somente para falhas transitórias e operações idempotentes;
- circuit breaker e bulkhead evitam cascata;
- cancelamento do cliente interrompe chamadas downstream;
- ausência de uma fonte reduz coverage/confidence;
- todas as fontes essenciais indisponíveis produzem <code>executionStatus=unavailable</code> determinístico.

Separar:

- <code>/live</code>: processo funcional;
- <code>/ready</code>: bootstrap concluído e dependências mínimas avaliadas;
- endpoint de status detalhado: providers, último scan e degradações.

O <code>/health</code> atual sempre retorna saudável, em [Program.cs](../../src/Services/McpServer/Program.cs#L114), e não pode continuar sendo a única readiness signal.

---

## 12. Segurança

Controles obrigatórios antes de disponibilizar as tools vNext:

- autenticação no MCP HTTP e dashboard;
- policies separadas de leitura e administração;
- namespace allowlist;
- fail-closed para identidade não resolvida;
- RBAC mínimo por recurso e namespace;
- nenhuma query raw na superfície MCP/LLM;
- validação de service name, namespace, janela, depth e limites;
- proteção SSRF para URLs vindas de annotations/configuração;
- timeout e limite de payload;
- redaction de PII, secrets e credenciais;
- audit log estruturado por tool;
- rate limit e limite de concorrência;
- TLS no ponto de entrada de produção;
- restrição de acesso a <code>/metrics</code> e endpoints administrativos.

### 12.1 SSRF e egress

O discovery aceita <code>mcp-apis/base-url</code> e o validator faz requisições ao valor resolvido. Restringir:

- schemes permitidos;
- hosts e sufixos DNS permitidos, normalmente <code>.svc.cluster.local</code>;
- namespaces autorizados;
- IPs loopback, link-local e metadata;
- redirects para hosts externos;
- portas permitidas.

### 12.2 RBAC esperado

Leituras futuras precisam ser adicionadas somente conforme a fase:

| Recurso | Verbs | Fase |
|---|---|---|
| Pods, Services, Endpoints, Deployments | get/list | Já existe |
| ReplicaSets | get/list | Health/catalog |
| Events | get/list | Timeline |
| HPA | get/list | Health/correlation |
| Nodes | get/list | Opcional, após justificativa |
| ConfigMap próprio do MCP | get e, se ADR aprovada, update/patch somente no objeto nomeado; sem list | Administração |

Nenhuma permissão de create/delete/update/patch em workloads é permitida.

### 12.3 Dependências vulneráveis

O build da baseline conclui, mas reporta avisos NU1902/NU1903 para dependências OpenTelemetry e Microsoft.OpenApi transitiva. Antes de qualquer release operacional:

- atualizar para versões corrigidas compatíveis;
- adicionar restore/build com auditoria de vulnerabilidades ao CI;
- bloquear vulnerabilidade alta/crítica sem exceção formal;
- revisar pacotes beta usados pela instrumentação.

---

## 13. Observabilidade do próprio MCP

A instrumentação automática existente deve ser preservada e enriquecida com <code>ActivitySource</code> e <code>Meter</code> próprios.

Spans esperados:

~~~text
MCP tool
  -> Engine
     -> Provider
        -> Backend HTTP/Kubernetes
~~~

Tags de baixa cardinalidade:

- tool;
- engine;
- provider;
- executionStatus complete/partial/unavailable;
- cache hit/miss;
- namespace autorizado;
- tipo de finding;
- quantidade de itens processados.

Não registrar serviceName irrestrito como label de métrica sem política de cardinalidade.

Métricas mínimas:

- duração, erro e chamadas por tool;
- duração, erro, timeout e circuit state por provider;
- cache hit ratio;
- traces, spans e logs processados;
- bytes da resposta e estimativa de tokens;
- duração de health, anomaly, timeline e RCA;
- resultados por executionStatus e availability;
- quantidade de findings por tipo/severidade.

Audit log mínimo:

- identidade do chamador;
- tool e schemaVersion;
- serviço/namespace;
- janela solicitada;
- resultado e duração;
- fontes acessadas;
- razão de negação;
- sem query raw, body sensível ou evidência não redigida.

---

## 14. Estratégia de migração

### 14.1 Regra geral

Não substituir collectors nem tools atuais de uma só vez. Introduzir a camada vNext e migrar por vertical slice:

~~~text
Tool vNext
  -> Engine/Use case
     -> Provider normalizado
        -> collector atual encapsulado
~~~

Depois, tools legadas passam a delegar ao mesmo provider quando isso não alterar sua semântica.

### 14.2 Política para tools legadas

- anunciar depreciação em descrição e documentação;
- manter por ao menos uma janela de release acordada;
- não mudar silenciosamente campos existentes;
- medir uso antes da remoção;
- remover <code>query_metrics</code> da exposição padrão já na fase de segurança;
- preservar <code>trace_route</code>, <code>find_data_origin</code> e <code>explain_api</code> como capacidades complementares, após limites e contratos consistentes;
- manter inventário completo somente para administradores.

### 14.3 Dashboard

O dashboard deve consumir os mesmos engines/providers das tools. Endpoints que acessam Jaeger/Prometheus diretamente deverão ser migrados para evitar duas regras de negócio.

Raw PromQL e range arbitrário ficam:

- removidos da UI padrão; ou
- isolados em área administrativa autenticada e desabilitada por feature flag.

---

## 15. Plano de implementação revisado

O cronograma original de oito semanas continua plausível. A base existente reduz o esforço de scaffolding, mas contratos, segurança e testes ocupam parte desse ganho. As durações abaixo são relativas e devem ser ajustadas à equipe.

### Fase 0 — Convergência arquitetural e segurança

**Duração sugerida:** semana 1  
**Prioridade:** bloqueante

Entregas:

- ADRs para read-only/ConfigMap, naming/depreciação, autenticação e fonte de métricas de infraestrutura;
- contratos vNext e envelope comum;
- interfaces de providers normalizados;
- wrappers iniciais sobre collectors existentes;
- options tipadas e validação de startup;
- namespace allowlist e identidade fail-closed;
- autenticação/autorização;
- query raw removida ou desabilitada na superfície MCP; eventual console administrativo permanece separado;
- limites, cancellation e telemetria semântica;
- projetos de testes e contract test do MCP.

Critério de saída:

- nenhuma engine depende de <code>JsonElement</code> ou aceita query raw externa;
- tool vNext de teste retorna envelope completo/partial;
- acesso sem autenticação é negado no ambiente protegido;
- namespace fora da allowlist é invisível;
- build e testes automatizados passam.

### Fase 1 — Catálogo enriquecido e service spec

**Duração sugerida:** semana 2

Entregas:

- identidade namespace-aware;
- seletores reais;
- imagem, versão, revisão e réplicas;
- owner/time e annotations permitidas;
- matriz de cobertura dos sinais;
- OpenAPI parseada/cacheada;
- <code>service_get_spec</code>;
- compatibilidade de <code>get_openapi</code>/<code>explain_api</code>.

Critério de saída:

- um serviço é resolvido de forma inequívoca;
- colisão entre namespaces retorna candidatos estruturados;
- a resposta inclui versão/imagem, endpoints resumidos e cobertura;
- OpenAPI indisponível produz partial, não falha total.

### Fase 2 — Metrics/Kubernetes providers e Health MVP

**Duração sugerida:** semanas 3–4, após o gate da Fase 1

Entregas:

- templates RED e métricas de recursos;
- cobertura Prometheus para serviços permitidos;
- Kubernetes provider corrigido/enriquecido;
- Health Engine v1;
- score, coverage, findings e evidências;
- <code>service_get_health</code> e <code>service_get_score</code>;
- cache de health.

Critério de saída:

- zero Pods não retorna saudável;
- fonte ausente reduz coverage e dimensão obrigatória ausente força health unknown;
- score é reproduzível por testes;
- thresholds globais e por serviço funcionam;
- cada finding referencia evidências;
- <code>get_health</code> legado continua estável.

### Fase 3 — Traces e Dependency Engine

**Duração sugerida:** semanas 3–4, em paralelo à Fase 2 e após o gate da Fase 1

Entregas:

- spans normalizados com causalidade e timestamp;
- slow/error traces e latência por dependência;
- Dependency Engine;
- inbound/outbound, depth, ciclos, critical path e blast radius;
- <code>service_get_dependencies</code>;
- correção de <code>find_data_origin</code>.

Critério de saída:

- o grafo é consultável por serviço;
- relações parent/child são preservadas;
- profundidade respeita teto;
- arestas possuem fonte/freshness;
- dependência desabilitada ou fora da allowlist não vaza.

### Fase 4 — Anomaly Engine e baselines

**Duração sugerida:** semana 5

Entregas:

- comparação entre janelas;
- comparações 24h/7d;
- EWMA ou robust Z-score;
- anomalias de latência, tráfego, erro e saturação;
- <code>service_detect_anomalies</code>;
- métricas de precisão/falso positivo.

Critério de saída:

- fixtures conhecidas detectam spike/drop/regressão;
- pouca amostra produz inconclusive;
- aumento de tráfego isolado não vira incidente automaticamente;
- método e baseline aparecem na evidência.

### Fase 5 — Logs, deploy events e correlação

**Duração sugerida:** preparação de Logs/Event Providers nas semanas 4–5; Correlation Engine e timeline na semana 6, após o gate da Fase 4

Entregas:

- Loki provider, fingerprints, samples e redaction, quando a fonte estiver disponível;
- correlação trace/log;
- Kubernetes Events e mudanças de versão/imagem;
- integração GitOps/CI/CD mínima escolhida;
- <code>IncidentEvent</code> e Correlation Engine;
- <code>service_get_incident_timeline</code>.

Critério de saída:

- timeline ordena deploy, anomalia, restart e trace, incorporando logs quando disponíveis;
- fontes indisponíveis ficam explícitas;
- logs indisponíveis produzem execução partial, sem impedir a timeline;
- duplicatas são agrupadas;
- PII das fixtures não aparece;
- histórico necessário sobrevive à retenção curta de Events.

### Fase 6 — RCA, recommendations e resumo sistêmico

**Duração sugerida:** semanas 7–8

Entregas:

- geração/ranking de hipóteses;
- confidence e evidências favoráveis/contrárias;
- blast radius;
- <code>service_find_root_cause</code>;
- Recommendation Engine;
- <code>system_get_health_summary</code>;
- métricas de acerto e tempo de análise.

Critério de saída:

- RCA sem evidência suficiente retorna inconclusive;
- confidence cai quando uma fonte relevante está indisponível;
- recomendações sempre são não executáveis;
- resumo sistêmico usa o mesmo Health Engine/cache;
- nenhum texto causal é inventado fora das evidências.

### Fase 7 — Validação AKS e rollout

**Duração:** estimada separadamente conforme a plataforma; não deve ser presumida dentro das oito semanas funcionais

Entregas:

- overlays de Development/k3d e AKS;
- imagem publicada em registry autorizado;
- TLS, DNS, autenticação e secrets;
- RBAC/allowlist e NetworkPolicy validados;
- configuração dos backends reais;
- teste de múltiplas réplicas, sessão, cache e estado administrativo;
- dashboards/alertas do próprio MCP;
- rollout progressivo e plano de rollback da aplicação MCP;
- execução dos testes E2E e de resiliência no ambiente de homologação.

Critério de saída:

- nenhum valor ou host local permanece no overlay AKS;
- permissões efetivas passam pela matriz <code>kubectl auth can-i</code>;
- o MCP mantém contratos e freshness sob restart/rollout;
- SLOs e limites definidos são medidos no ambiente alvo;
- vulnerabilidades e riscos de release possuem gate ou exceção formal.

---

## 16. Paralelização

Depois da Fase 1:

~~~mermaid
flowchart LR
    A[Contratos + catálogo] --> B[Frente A: Metrics + Health]
    A --> C[Frente B: Traces + Dependencies]
    B --> D[Anomalies]
    C --> E[Logs + Events]
    D --> F[Correlation]
    E --> F
    F --> G[RCA + Recommendations]
~~~

Responsabilidades transversais — segurança, cache, contratos, observabilidade e testes — não devem ficar para a última fase.

---

## 17. Estratégia de testes

### 17.1 Unitários

- normalização e ambiguidade de identidade;
- seleção de Pods por selector;
- zero Pods e estados Kubernetes;
- health score, coverage e thresholds;
- builders internos de query;
- comparação de janelas/EWMA/Z-score;
- merge de grafos, ciclos, depth e blast radius;
- ranking de hipóteses;
- redaction;
- cache TTL/invalidation/single-flight;
- limites e validação de entrada.

### 17.2 Contrato

- snapshot/JSON Schema de cada tool vNext;
- <code>schemaVersion</code>;
- envelopes com executionStatus e availability separados;
- erros estáveis;
- tools/list e tools/call;
- compatibilidade das tools legadas;
- testes nos clientes MCP suportados.

### 17.3 Integração de providers

Usar servidores fake/fixtures e Kubernetes fake para:

- sucesso;
- 4xx, 429 e 5xx;
- timeout e cancelamento;
- JSON malformado;
- payload excessivo;
- dados vazios;
- cache hit/miss/stale;
- mudança de versão e invalidação.

### 17.4 Resiliência

Matriz de outage:

| Prometheus | Jaeger | Kubernetes | Logs | Resultado esperado |
|---|---|---|---|---|
| OK | OK | OK | indisponível | partial com warning e confidence reduzida |
| OK | indisponível | OK | OK | health possível; dependências/RCA limitados |
| indisponível | OK | OK | OK | pod health parcial; score unknown se coverage insuficiente |
| indisponível | indisponível | indisponível | indisponível | unavailable determinístico |

### 17.5 Segurança

- 401/403 e policies;
- namespace fora da allowlist;
- RBAC positivo para leituras e negativo para mutação de workloads;
- decisão do ConfigMap próprio validada;
- PromQL/LogQL raw recusado na superfície MCP; eventual console administrativo tem policy e testes próprios;
- SSRF, redirects e metadata endpoints bloqueados;
- rate, janela, depth, payload e trace limits;
- PII/secret ausentes de traces, logs, cache e respostas.

### 17.6 E2E

Criar cenários determinísticos:

- deploy seguido de regressão de P95;
- spike de tráfego sem degradação;
- erro downstream propagado ao upstream;
- OOM/restart;
- ausência de Pods;
- fonte indisponível;
- duas aplicações de mesmo nome em namespaces diferentes.

Os scripts atuais validam presença das tools, infraestrutura e smoke tests, mas não chamam os contratos de inteligência nem verificam conteúdo. Eles devem continuar como smoke tests e ser complementados, não tratados como suíte de aceite.

---

## 18. Critérios de aceite consolidados

### MVP 1 — Catálogo e especificação

- lista aplicações permitidas;
- resolve identidade e namespace;
- mostra versão, imagem, réplicas, owner quando disponível e cobertura;
- resume endpoints/contratos;
- informa ausência de OpenAPI sem esconder o serviço.

### MVP 2 — Saúde

- retorna RED, recursos, estabilidade, score e healthStatus;
- inclui janela, freshness, coverage, findings e evidências;
- não retorna saudável com zero Pods ou dimensão obrigatória ausente;
- degrada parcialmente quando uma fonte falha.

### MVP 3 — Dependências

- retorna upstream/downstream por serviço e profundidade;
- preserva causalidade de traces;
- informa fonte/freshness e impacto potencial;
- trata ciclos e ambiguidade.

### MVP 4 — Anomalias

- detecta mudanças de latência, tráfego, erros e saturação;
- compara janela atual/anterior/24h/7d;
- publica método, baseline e confiança;
- mede falso positivo.

### MVP 5 — Timeline

- ordena anomalias, deploys, Events, restarts e traces e incorpora logs quando disponíveis;
- deduplica eventos;
- mostra fontes indisponíveis;
- permite explicar o intervalo anterior e posterior ao início.

### MVP 6 — RCA

- produz hipótese principal e alternativas;
- apresenta evidências favoráveis e contrárias;
- publica confidence, coverage e limitações;
- retorna inconclusive quando necessário;
- gera somente recomendações não executáveis.

### Gates transversais

- autenticação/autorização habilitadas no ambiente protegido;
- allowlist aplicada no código e no RBAC;
- nenhuma raw query na superfície MCP/LLM;
- redaction e audit log verificados;
- contratos versionados;
- testes automatizados verdes;
- trace Tool → Engine → Provider → Backend;
- métricas de latência, erro, cache, executionStatus e availability;
- resposta dentro dos limites de bytes/itens definidos.

---

## 19. Riscos e decisões pendentes

| ID | Decisão | Recomendação | Prazo |
|---|---|---|---|
| ADR-001 | Read-only literal ou exceção para estado próprio | Exceção estreita, auditada, sem mutação de workloads | Antes da Fase 0 |
| ADR-002 | Nome físico das tools | Prefixo <code>service_</code> e compatibilidade legada; validar clientes | Fase 0 |
| ADR-003 | Autenticação | Integrar o mecanismo padrão do ambiente e separar reader/admin | Fase 0 |
| ADR-004 | Fonte de CPU/memória/saturação | Prometheus corporativo ou stack K8s com cobertura explícita | Antes do Health |
| ADR-005 | Histórico de deploy | Kubernetes para MVP + provider GitOps/CI/CD durável | Antes da Timeline |
| ADR-006 | Persistência de baseline | Consultar Prometheus primeiro; materializar apenas mediante evidência | Antes de Anomalies |
| ADR-007 | Janela de depreciação | Medir uso e definir ao menos uma release compatível | Fase 0 |
| ADR-008 | Cobertura multi-namespace | Allowlist positiva comum a discovery, métricas, logs e RBAC | Fase 0 |
| ADR-009 | Topologia AKS e alta disponibilidade | Definir overlays, registry, ingress, identidade, réplicas e estado compartilhado | Antes do rollout |

Riscos técnicos adicionais:

- nomes normalizados podem colidir no mesmo namespace;
- o mesmo nome OTel pode representar workloads de namespaces diferentes;
- retenção de Jaeger e Kubernetes Events pode ser insuficiente para timeline;
- ausência de trace ID nos logs inviabiliza correlação forte;
- métricas OTel variam por versão e backend;
- respostas OpenAPI/raw podem exceder orçamento de tokens;
- cache stale sem freshness pode produzir diagnóstico enganoso;
- confidence sem calibração pode transmitir certeza indevida.
- documentação de casos de uso pode descrever capacidades aspiracionais como se já existissem; os contratos publicados devem ser gerados ou verificados contra as tools reais.

---

## 20. Itens explicitamente fora do plano

- rollback automático;
- scale de Deployment ou alteração de HPA;
- restart de Pods;
- alteração de ConfigMaps/Secrets das aplicações;
- execução de PromQL/LogQL fornecida pelo LLM;
- substituição de Prometheus, Jaeger ou Loki;
- ML complexo antes de regras e baselines simples;
- recomendação executável;
- geração de causa raiz pelo LLM sem evidence package.

---

## 21. Definition of Done do programa

O programa estará concluído quando:

1. as tools vNext responderem aos seis MVPs com contratos versionados;
2. cada diagnóstico possuir evidências, freshness, coverage e limitações;
3. os engines forem independentes dos backends concretos;
4. indisponibilidade parcial não causar falso saudável nem falha total evitável;
5. autenticação, allowlist, RBAC, limites, redaction e auditoria estiverem ativos;
6. ferramentas legadas tiverem migração/depreciação documentada;
7. testes unitários, contrato, integração, segurança, resiliência e E2E passarem;
8. o próprio MCP expuser métricas e traces semânticos;
9. nenhum engine ou cliente MCP/LLM executar query arbitrária;
10. recomendações permanecerem somente informativas.

---

## 22. Conclusão

A base atual já contém o núcleo de descoberta e conectividade necessário. O plano correto não começa criando outro MCP Server nem substituindo o catálogo. Ele começa **endurecendo a fronteira pública, normalizando providers e enriquecendo a identidade**.

Depois disso, a evolução natural é:

~~~text
catálogo confiável e cobertura conhecida
          |
          v
spec consolidada
          |
          v
health + score + evidências
          |
          v
dependências causais
          |
          v
anomalias
          |
          v
timeline correlacionada
          |
          v
RCA explicável
          |
          v
recomendações não executáveis
~~~

Essa sequência preserva o investimento já realizado, resolve os conflitos antes que se tornem dívida estrutural e mantém a decisão central da proposta: **cálculo e evidência no MCP; interpretação e linguagem natural no LLM**.
