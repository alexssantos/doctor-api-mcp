# `get_health`

## Objetivo

Verifica a saude operacional de uma aplicacao inspecionando seus pods no Kubernetes: estado de prontidao, reinicios e estado dos containers.

Para uma crianca de 10 anos: e como checar se os funcionarios de uma loja chegaram, estao prontos para trabalhar e nao precisaram recomecar o turno varias vezes.

## Contrato MCP

- **Nome:** `get_health`
- **Parametro obrigatorio:** `appName` (`string`), por exemplo `precoapi`, `produtoapi`, `jaeger`, `prometheus` ou `grafana`.
- **Retorno:** texto JSON formatado com o resultado do coletor Kubernetes.

## Como funciona

1. Confere se a aplicacao esta habilitada no catalogo. Aplicacoes desabilitadas recebem um JSON de erro sem consultar Kubernetes.
2. Quando a aplicacao foi descoberta, resolve o identificador mais especifico nesta ordem: `deploymentName`, `kubernetesServiceName`, depois o valor enviado.
3. Usa tambem o namespace descoberto, o que permite consultar aplicacoes fora do namespace padrao do MCP Server.
4. Chama `IKubernetesCollector.GetHealthAsync` e serializa o resultado.

## Exemplo de uso

```text
Use get_health com appName="produtoapi" e informe pods nao prontos, reinicios e containers com falha.
```

Exemplo ilustrativo de resposta:

```json
{
  "app": "produtoapi",
  "healthy": true,
  "pods": [
    { "name": "produtoapi-abc", "ready": true, "restarts": 0 }
  ]
}
```

Os campos exatos dependem do coletor Kubernetes; a tool preserva sua estrutura.

## Limitacoes

- Mede o estado dos pods, nao o comportamento funcional de cada endpoint.
- Uma aplicacao pode ter pods prontos e ainda assim falhar ao acessar uma dependencia externa.
- Para entender chamadas entre servicos, use `trace_route` ou `find_dependencies`.
