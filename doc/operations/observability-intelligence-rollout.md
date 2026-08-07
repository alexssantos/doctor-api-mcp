# Operação e rollout da Observability Intelligence

Este runbook é a referência operacional das fases 0–7 da expansão do
McpServer. Ele cobre o perfil local k3d/WSL, os gates de uma futura publicação
AKS, segurança, múltiplas réplicas, sinais do próprio MCP e rollback. As tools
continuam estritamente read-only em relação aos workloads observados;
recommendations nunca executam restart, scale ou rollback.

## Superfície entregue

O servidor mantém as oito tools legadas compatíveis e publica, por padrão, as
oito tools normalizadas vNext:

| Tool | Resultado |
|---|---|
| `service_get_spec` | identidade, versão, imagem, cobertura e endpoints resumidos |
| `service_get_health` | saúde composta, score, dimensões, findings e evidências |
| `service_get_score` | projeção compacta do score de saúde |
| `service_get_dependencies` | grafo inbound/outbound limitado por profundidade |
| `service_detect_anomalies` | comparação de janela atual/baseline e severidade |
| `service_get_incident_timeline` | timeline correlacionada de sinais e mudanças |
| `service_find_root_cause` | hipóteses explicáveis, confidence e recomendações não executáveis |
| `system_get_health_summary` | visão sistêmica calculada pelo mesmo Health Engine |

`query_metrics` e os endpoints REST de PromQL bruto não são mapeados quando
`Observability__Features__EnableRawQueries=false`, que é o default e o valor do
perfil k3d. Toda resposta vNext informa status de execução, status dos dados,
janela, freshness, fontes, evidências, warnings e erros estáveis.

## Perfis de implantação

### Development/k3d

O overlay `infra/k8s/overlays/k3d` é o perfil executado por
`infra/scripts/ps/up-k8s.ps1`. Ele usa imagens importadas no k3d,
`imagePullPolicy: Never`, hosts `*.local` e autenticação desativada
explicitamente. Essa exceção só é aceitável no cluster de laboratório local.

O laboratório usa o Traefik v3 empacotado e atualizado pelo próprio K3s. Os
scripts não baixam nem instalam um controller externo. O Ingress-NGINX foi
aposentado em março de 2026; clusters legados que o usavam são bloqueados pelo
script e precisam ser recriados com preservação prévia dos dados.

Novos clusters são fixados em `rancher/k3s:v1.36.1-k3s1` com binaries
empacotados. O k3d 5.8.3 usa K3s 1.31.5 por default; nesse runtime antigo o
controller chegou a materializar a policy e seus ipsets, mas não ligou os
chains aos Pods. A tentativa cross-namespace alcançou o serviço e o gate falhou.
Uma prova isolada em K3s 1.36.1 criou os chains `KUBE-POD-FW`, permitiu a origem
autorizada e bloqueou a origem não autorizada. Portanto, manifesto aplicado não
é evidência suficiente: o teste ativo é obrigatório.

Em 2026-08-07, o stack completo foi revalidado do zero em um cluster WSL
descartável com K3s 1.36.1 e Traefik 3.6.13: os 38 gates passaram, incluindo
afinidade de sessão, rollout, persistência/freshness e negação ativa da
NetworkPolicy. O cluster de prova foi removido após a coleta da evidência.

O workload roda com:

- duas réplicas e rolling update `maxUnavailable=0`, `maxSurge=1`;
- readiness em `/ready`, liveness/startup em `/live`;
- `PodDisruptionBudget` com uma réplica mínima disponível;
- usuário não-root, seccomp `RuntimeDefault`, capabilities removidas,
  filesystem raiz read-only e `/tmp` efêmero limitado;
- afinidade de cookie `mcp-route` no Service Traefik para manter sessões
  Streamable HTTP na mesma réplica;
- NetworkPolicy de ingress/egress e ServiceAccount `mcp-reader`.

### AKS

O overlay `infra/k8s/overlays/aks` é um template deliberadamente bloqueado por
placeholders. Ele muda para três réplicas, autenticação obrigatória, secret
externo e imagem de registry. O `Ingress` Traefik do k3d é removido e substituído por uma
`HTTPRoute` genérica ligada a um Gateway API administrado pela plataforma. Não
o aplique antes de cumprir o README do próprio overlay.

Os seguintes dados pertencem à plataforma alvo e não podem ser inferidos do
repositório:

- registry autorizado, tag/digest publicado e identidade de pull;
- zona DNS, Gateway API/controller suportado, listener HTTPS e certificado;
- `Secret/mcpserver-auth` obtido de Key Vault/External Secrets/CSI;
- URLs e autenticação dos backends Prometheus, Jaeger, Loki e OTLP;
- namespaces reais observados e política de egress correspondente;
- contexto AKS de homologação e janela autorizada de rollout.
- política de afinidade de sessão suportada pelo controller escolhido.

Um release AKS não está aprovado enquanto qualquer placeholder existir, a
imagem não estiver publicada e assinada, ou os testes desta página não forem
reexecutados naquele contexto.

## Segurança efetiva

### Autenticação e policies

Quando habilitada, a autenticação aceita a chave no header configurado
`X-Observability-Api-Key` ou como bearer token. As policies são separadas:

- `ObservabilityReader`: MCP, dashboard e REST read-only;
- `ObservabilityAdmin`: toggle de indexação, rescan e, somente se a feature for
  explicitamente habilitada, endpoints raw administrativos.

As chaves nunca devem entrar em ConfigMap, imagem, log ou repositório. O handler
compara chaves em tempo constante. Rate limit e limite de concorrência são
aplicados por identidade.

### RBAC

O ClusterRole lê apenas Pods, Services, Endpoints, Events e Deployments. O Role
namespaced pode ler ConfigMaps, mas só pode atualizar/patchar
`ConfigMap/mcpserver-state`. Ele não pode ler Secrets nem mutar workloads.

Matriz mínima a validar no destino:

```bash
SA=system:serviceaccount:mcp-apis:mcp-reader
kubectl auth can-i --as="$SA" get pods --all-namespaces                 # yes
kubectl auth can-i --as="$SA" list deployments.apps --all-namespaces  # yes
kubectl auth can-i --as="$SA" list events -n mcp-apis                 # yes
kubectl auth can-i --as="$SA" patch cm/mcpserver-state -n mcp-apis   # yes
kubectl auth can-i --as="$SA" patch cm/mcpserver-config -n mcp-apis  # no
kubectl auth can-i --as="$SA" get secrets -n mcp-apis                # no
kubectl auth can-i --as="$SA" delete pods -n mcp-apis                # no
```

### NetworkPolicy e SSRF

Ingress na porta 4000 é aceito somente do namespace da aplicação e, no k3d, do
Traefik em `kube-system`. O overlay AKS troca essa origem por namespaces explicitamente
rotulados com `ingress.mcp-apis.io/allow=true`. Egress permite DNS, os
backends/serviços nos namespaces que optaram por
`observability.mcp-apis.io/allow=true` e o Kubernetes API em 443.
Adicionar um namespace à allowlist de aplicação sem rotulá-lo para egress
produzirá resultado parcial/unavailable, não uma abertura automática de rede.

A política de rede complementa as validações da aplicação: URLs precisam usar
HTTP(S), DNS permitido, porta permitida, namespace esperado e não podem conter
credenciais, fragmentos ou IP literal. Redirects automáticos estão desativados.

## Consistência com múltiplas réplicas

O catálogo e os caches de análise são locais a cada processo. As chaves de
cache incluem serviço, namespace, janela e parâmetros; TTLs curtos limitam a
divergência. O Ingress mantém uma sessão MCP na mesma réplica.

O estado administrativo e o histórico resumido de deployments usam
`mcpserver-state`, com `resourceVersion`/retry no update. Um toggle confirmado
persiste no ConfigMap e converge nas outras réplicas no próximo scan. Portanto:

- sessões em andamento exigem o cookie de afinidade;
- cache não é uma fonte de verdade e pode diferir por até o TTL;
- estado administrativo deve sobreviver a restart/rollout;
- Kubernetes Events têm retenção limitada; o ConfigMap guarda apenas o resumo
  de mudanças observado pelo catálogo.

Para uma escala muito maior ou necessidade de consistência imediata, migrar
sessão/cache/estado para um store externo é uma decisão de plataforma, não uma
permissão para aumentar o RBAC do servidor.

## SLOs, métricas e alertas

Prometheus carrega as recording rules:

- `mcpserver:slo_availability:ratio5m`;
- `mcpserver:http_request_rate:5m`;
- `mcpserver:http_error_ratio:5m`;
- `mcpserver:http_latency_p95_seconds:5m`.

Alertas iniciais:

| Alerta | Condição | `for` |
|---|---|---:|
| `McpServerUnavailable` | scrape `up == 0` | 1 min |
| `McpServerHighErrorRatio` | HTTP 5xx > 5% | 5 min |
| `McpServerHighLatency` | p95 > 2 s | 5 min |

O dashboard provisionado `MCP Server - Observability Intelligence` mostra
target, request rate, erro, p95, chamadas de tools, outcomes de providers, hit
ratio de cache e alertas firing. Antes de promover para produção, calibre os
limiares com tráfego real e converta-os em SLO/error budget aprovado.

As métricas próprias evitam labels de alta cardinalidade. Traces usam a source
`McpApis.ObservabilityIntelligence`; logs de auditoria registram caller, método,
rota, status e duração, nunca bodies/chaves.

## Build e validação local no WSL

Na raiz do repositório, no PowerShell do Windows:

```powershell
dotnet restore src/mcp-apis.slnx --ignore-failed-sources
dotnet build src/mcp-apis.slnx --no-restore
dotnet test src/Services/McpServer.Tests/McpApis.McpServer.Tests.csproj --no-build --no-restore
dotnet list src/mcp-apis.slnx package --vulnerable --include-transitive --source https://api.nuget.org/v3/index.json

Push-Location src/Services/McpServer/Dashboard
npm ci
npm run lint
npm run build
npm run build:pages
npm run test:e2e
Pop-Location

.\infra\scripts\ps\up-k8s.ps1 -Build
```

O `-Build` constrói Docker dentro do WSL, importa as imagens no k3d e aplica o
overlay. Isso é necessário quando uma imagem `:latest` antiga permanece no
cache do cluster.

Execute então o gate completo, inclusive restart e negação ativa de rede:

```powershell
$repo = (Get-Location).Path
$drive = $repo.Substring(0, 1).ToLower()
$wslRepo = "/mnt/$drive" + ($repo.Substring(2) -replace '\\', '/')
wsl.exe -- bash -lc "bash $wslRepo/infra/scripts/sh/validate-phase7.sh --resilience"
```

O teste valida render/server dry-run, duas réplicas, probes, PDB,
NetworkPolicy, matriz RBAC, endpoints, limites, ausência de raw PromQL,
telemetria, alertas, dashboard Grafana, cookie/sessão MCP, oito tools vNext,
persistência/freshness após rollout e bloqueio de namespace não autorizado.

Se o cluster existente for anterior ao K3s 1.36 ou tiver sido criado com
Traefik desabilitado, `up-k8s.ps1` bloqueia antes de aplicar os novos manifests.
k3d não oferece upgrade in-place da imagem do server. Exporte
`mcpserver-state`, faça dump dos bancos/PVCs que precisam ser preservados e
obtenha aprovação explícita antes de apagar e recriar `mcp-apis`; essa migração
é destrutiva para o estado local.

## Rollout progressivo

1. Produza uma imagem imutável, gere SBOM, faça scan de vulnerabilidades e
   publique/assine no registry autorizado.
2. Renderize o overlay e arquive o YAML como artefato do release.
3. Execute server-side dry-run, validação de policies e a matriz RBAC.
4. Aplique primeiro em homologação. Aguarde `/ready`, `availableReplicas` e os
   quatro SLOs; verifique logs e alertas.
5. Gere tráfego de leitura e execute `validate-phase7.sh --resilience` adaptado
   ao contexto/FQDN da homologação.
6. Promova o mesmo digest. Não reconstrua a imagem entre ambientes.
7. Observe ao menos uma janela de cinco minutos sem alertas e confira
   freshness/source coverage antes de encerrar a mudança.

Comandos de acompanhamento:

```bash
kubectl rollout status deployment/mcpserver -n mcp-apis --timeout=5m
kubectl get deploy,rs,pod,pdb -n mcp-apis -l app=mcpserver
kubectl logs -n mcp-apis -l app=mcpserver --since=10m --prefix
kubectl describe deployment/mcpserver -n mcp-apis
```

## Rollback

Rollback é uma ação humana/plataforma; nenhuma tool MCP a executa.

Critérios iniciais para abortar ou reverter:

- `/ready` não estabiliza dentro de `progressDeadlineSeconds`;
- disponibilidade cai, 5xx ultrapassa 5% ou p95 permanece acima de 2 s;
- contratos vNext mudam, fontes deixam de reportar freshness ou há vazamento de
  dados sensíveis;
- sessão quebra entre requests ou o estado administrativo não sobrevive;
- RBAC/NetworkPolicy concede acesso além da matriz aprovada.

Procedimento:

```bash
kubectl rollout history deployment/mcpserver -n mcp-apis
kubectl rollout undo deployment/mcpserver -n mcp-apis
kubectl rollout status deployment/mcpserver -n mcp-apis --timeout=5m
```

Depois do undo, reexecute os contratos/readiness, confirme o conteúdo de
`mcpserver-state`, registre o digest/revisão revertida e preserve logs, Events,
traces e alertas para análise. ConfigMap/Secret incompatível deve ser revertido
com o artefato versionado correspondente; `rollout undo` reverte apenas o
template do Deployment.
