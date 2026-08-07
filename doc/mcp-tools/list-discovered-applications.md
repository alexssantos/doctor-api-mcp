# `list_discovered_applications`

## Objetivo

Mostra todas as aplicacoes encontradas pelo processo de descoberta do MCP Server, inclusive as desabilitadas. E a fonte adequada para entender por que uma aplicacao pode ou nao ser indexada e consultada por tools baseadas em OpenAPI.

Para uma crianca de 10 anos: imagine uma lista de todos os brinquedos encontrados no patio. Ela mostra de onde cada brinquedo veio e se ele pode ser usado agora.

## Contrato MCP

- **Nome:** `list_discovered_applications`
- **Parametros:** nenhum
- **Retorno:** texto JSON com um array de aplicacoes.

## Como funciona

A tool le o `IApplicationCatalog`, preenchido pela descoberta automatica. Cada aplicacao pode ter sido identificada por uma ou mais fontes:

| Valor em `sources` | Significado |
| --- | --- |
| `deployment` | Encontrada em um Deployment Kubernetes. |
| `network` | Encontrada por Service, Endpoints ou rede Kubernetes. |
| `otel` | Encontrada em dados de OpenTelemetry/Jaeger. |
| `config` | Conhecida por configuracao. |

Diferente de `list_services`, esta tool nao oculta aplicacoes desabilitadas.

## Estrutura de resposta

```json
[
  {
    "name": "produtoapi",
    "ns": "mcp-apis",
    "sources": ["deployment", "network", "otel"],
    "deploymentName": "produtoapi",
    "kubernetesServiceName": "produtoapi",
    "otelServiceName": "ProdutoAPI",
    "baseUrl": "http://produtoapi",
    "hasReadyEndpoints": true,
    "openApi": {
      "validated": true,
      "path": "/openapi/v1.json",
      "failures": []
    },
    "enabled": true,
    "lockedDisabled": false,
    "firstSeen": "2026-08-05T10:00:00+00:00",
    "lastSeen": "2026-08-05T10:10:00+00:00"
  }
]
```

`openApi.validated` indica se a especificacao foi encontrada e validada. `lockedDisabled` indica que o estado desabilitado nao deve ser reativado automaticamente pela descoberta.

## Exemplo de uso

```text
Use list_discovered_applications e identifique aplicacoes sem OpenAPI validado ou sem endpoints prontos.
```

## Limitacoes

- Descrever uma aplicacao no catalogo nao garante que ela tenha pods saudaveis no instante da consulta.
- A informacao e uma fotografia do ultimo ciclo de descoberta; use `get_health` para checagem operacional.
