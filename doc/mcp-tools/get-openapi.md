# `get_openapi`

## Objetivo

Recupera a especificacao OpenAPI de um servico conhecido. A especificacao descreve rotas, metodos HTTP, parametros, corpos e respostas que a API publica.

Para uma crianca de 10 anos: e como pedir o manual de uma maquina. O manual diz quais botoes existem e o que cada um faz.

## Contrato MCP

- **Nome:** `get_openapi`
- **Parametro obrigatorio:** `serviceName` (`string`), por exemplo `precoapi` ou `produtoapi`.
- **Retorno:** texto com a especificacao OpenAPI devolvida pelo coletor.

## Como funciona

A tool delega a consulta para `IOpenApiCollector.GetOpenApiSpecAsync(serviceName)`. O coletor resolve o servico conhecido e busca sua especificacao OpenAPI. A tool nao transforma o documento: o resultado e retornado diretamente.

## Exemplo de uso

```text
Use get_openapi com serviceName="produtoapi" e liste os endpoints, metodos e schemas disponiveis.
```

Exemplo de trecho esperado em uma especificacao:

```json
{
  "paths": {
    "/api/products": {
      "get": { "summary": "Lista produtos" }
    }
  }
}
```

## Quando usar

- Antes de chamar uma API quando e preciso conhecer seu contrato.
- Para encontrar o caminho exato de uma rota usada em `find_data_origin`.
- Para conferir se uma aplicacao oferece OpenAPI para a indexacao do MCP.

## Limitacoes

- O formato exato depende da API produtora da especificacao.
- Uma aplicacao descoberta sem OpenAPI valido pode nao ter especificacao recuperavel.
- Esta tool nao consulta traces nem mede disponibilidade; use `explain_api` ou `get_health` para esses casos.
