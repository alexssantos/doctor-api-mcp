# `list_services`

## Objetivo

Lista os recursos Kubernetes que o MCP Server enxerga no namespace configurado: `Services`, `Deployments` e `Pods`. Tambem informa os servicos de API conhecidos pelo coletor OpenAPI e as aplicacoes que foram desabilitadas para indexacao.

Para uma crianca de 10 anos: e como pedir a lista de salas, portas e pessoas de uma escola para descobrir quem esta funcionando e onde esta cada turma.

## Contrato MCP

- **Nome:** `list_services`
- **Parametros:** nenhum
- **Retorno:** texto JSON formatado.

## Como funciona

1. Consulta Kubernetes para obter servicos, deployments e pods.
2. Consulta o coletor OpenAPI para obter os servicos de API conhecidos.
3. Consulta o catalogo de aplicacoes.
4. Remove dos recursos retornados tudo que pertence a uma aplicacao com `enabled: false`.
5. Inclui os nomes ocultos em `disabledApplications`, para deixar explicito que houve filtragem.

A filtragem considera o nome do `Service`, do `Deployment` e o campo `app` de cada `Pod`.

## Estrutura de resposta

```json
{
  "services": [{ "name": "produtoapi" }],
  "deployments": [{ "name": "produtoapi" }],
  "pods": [{ "app": "produtoapi", "name": "produtoapi-..." }],
  "apiServices": ["produtoapi", "precoapi"],
  "disabledApplications": ["aplicacao-excluida"]
}
```

Os campos internos dos tres primeiros arrays sao definidos pelos coletores Kubernetes. O MCP Server preserva esses objetos e apenas aplica o filtro de indexacao.

## Exemplo de uso

```text
Use list_services para mostrar o estado dos componentes que o MCP pode consultar.
```

Use esta tool como ponto de partida antes de solicitar saude, traces ou especificacoes OpenAPI de uma aplicacao.

## Limitacoes

- A visao depende das permissoes RBAC da conta de servico do MCP Server.
- Aplicacoes desabilitadas nao aparecem nos arrays de recursos, mesmo que estejam ativas no cluster.
- Nao executa verificacoes de saude HTTP; para isso, use `get_health`.
