# 🔍 Auto-descoberta e Validação de Serviços no MCP Server

## Visão Geral

A partir desta feature, o MCP Server não depende mais de uma lista fixa de serviços no código. Em vez disso, ele **descobre** os serviços disponíveis em runtime e **valida** cada um antes de indexá-lo — garantindo que apenas serviços saudáveis e compatíveis sejam expostos pelas tools do MCP.

---

## 3. Auto-descoberta de Serviços

### Como funciona

Ao iniciar, o MCP Server executa o pipeline de descoberta via `ServiceDiscoveryService`. O resultado é um mapa `nome → baseUrl` de **candidatos** a serem validados.

A fonte de descoberta é controlada pela configuração `Discovery:Mode`:

| Modo         | Comportamento                                                                 |
|--------------|-------------------------------------------------------------------------------|
| `Config`     | Lê a seção `Services` do `appsettings.json` / variáveis de ambiente          |
| `Kubernetes` | Lista serviços no namespace K8s que possuem a label `mcp-apis/indexed=true`  |
| `Both`       | Mescla as duas fontes; entradas do K8s sobrescrevem entradas do config        |

### Modo `Config` (padrão)

Lê a seção `Services` da configuração. Cada chave é o nome do serviço e o valor é a URL base.

**`appsettings.json`:**
```json
{
  "Services": {
    "precoapi":   "http://precoapi",
    "produtoapi": "http://produtoapi"
  },
  "Discovery": {
    "Mode": "Config"
  }
}
```

**Variáveis de ambiente equivalentes (K8s ConfigMap):**
```yaml
Services__precoapi:   "http://precoapi"
Services__produtoapi: "http://produtoapi"
Discovery__Mode:      "Config"
```

### Modo `Kubernetes`

O MCP Server consulta a API do Kubernetes e lista todos os `Service` no namespace que possuam a label `mcp-apis/indexed: "true"`.

A URL base de cada serviço é resolvida na seguinte ordem de prioridade:
1. Annotation `mcp-apis/base-url` no `Service` (se presente)
2. Derivado como `http://<nome-do-service>` (fallback)

**Exemplo de `service.yaml` anotado:**
```yaml
apiVersion: v1
kind: Service
metadata:
  name: meuservico
  namespace: mcp-apis
  labels:
    mcp-apis/indexed: "true"
  annotations:
    mcp-apis/base-url: "http://meuservico"
spec:
  selector:
    app: meuservico
  ports:
    - port: 80
      targetPort: 8080
```

**Configuração necessária:**
```yaml
Discovery__Mode:           "Kubernetes"
Discovery__KubernetesLabel: "mcp-apis/indexed"
```

> ⚠️ Este modo requer que a `ServiceAccount` do MCP Server tenha permissão `list` em `services` no namespace. O RBAC já está configurado em `infra/k8s/mcpserver/rbac.yaml`.

### Modo `Both`

Combina `Config` e `Kubernetes`. Útil para ambientes onde alguns serviços são estáticos (configurados via env vars) e outros são dinâmicos (descobertos via K8s).

Entradas descobertas via Kubernetes **sobrescrevem** entradas de mesmo nome vindas do config.

### Adicionando um novo serviço

#### Via config (modo `Config` ou `Both`)

Adicione a variável de ambiente:
```
Services__meunovoservico=http://meunovoservico
```

Ou no `appsettings.json`:
```json
"Services": {
  "meunovoservico": "http://meunovoservico"
}
```

#### Via Kubernetes (modo `Kubernetes` ou `Both`)

Adicione as labels e annotations ao `Service` do serviço no cluster:
```yaml
labels:
  mcp-apis/indexed: "true"
annotations:
  mcp-apis/base-url: "http://meunovoservico"
```

Não é necessário reiniciar o MCP Server para que novos serviços sejam descobertos — a descoberta ocorre **no próximo start** do pod.

---

## 4. Validação de Serviços Antes de Indexar

### Como funciona

Após a descoberta, cada candidato passa pelo `ServiceValidator`. Apenas serviços que **passam em todos os critérios** são registrados no `ServiceRegistry` e ficam disponíveis para as tools do MCP.

### Critérios de validação

| # | Critério                        | Detalhe                                                                    |
|---|----------------------------------|----------------------------------------------------------------------------|
| 1 | **Serviço acessível**            | Responde com HTTP < 500 em `/health` ou `/` (timeout: 10s)                 |
| 2 | **OpenAPI spec acessível**       | `GET /openapi/v1.json` retorna HTTP 200                                    |
| 3 | **Spec com conteúdo válido**     | JSON parseável com pelo menos um `path` definido                           |

Se **qualquer critério falhar**, o serviço é ignorado e uma mensagem de aviso é emitida nos logs.

### Exemplos de log no startup

```
info: Starting service discovery...
info: Service discovery (Config) found 2 candidate(s): precoapi, produtoapi
info: ✓ Registered service 'precoapi' at http://precoapi
warn: ✗ Skipped service 'produtoapi' at http://produtoapi: OpenAPI spec not accessible: HTTP 404
info: Service discovery complete. 1 service(s) registered: precoapi
```

### Comportamento em falhas

| Situação                              | Resultado                                                    |
|---------------------------------------|--------------------------------------------------------------|
| Serviço fora do ar                    | Ignorado com warning; demais serviços continuam normalmente  |
| Spec sem paths                        | Ignorado com warning                                         |
| Spec não é JSON válido                | Ignorado com warning                                         |
| Discovery falha completamente         | Nenhum serviço é registrado; servidor sobe sem tools de API  |
| Tool chamada com serviço não indexado | Retorna mensagem de erro com a lista dos serviços disponíveis |

---

## Configuração de referência

```json
{
  "Discovery": {
    "Mode": "Config",
    "KubernetesLabel": "mcp-apis/indexed"
  },
  "Services": {
    "precoapi":   "http://precoapi",
    "produtoapi": "http://produtoapi"
  }
}
```

| Chave                        | Padrão              | Descrição                                              |
|------------------------------|---------------------|--------------------------------------------------------|
| `Discovery:Mode`             | `Config`            | Fonte de descoberta: `Config`, `Kubernetes` ou `Both`  |
| `Discovery:KubernetesLabel`  | `mcp-apis/indexed`  | Label K8s que marca serviços para indexação            |
| `Services:<nome>`            | —                   | URL base de um serviço (modo `Config` ou `Both`)       |
