# `find_dependencies`

## Objetivo

Monta o grafo de dependencias observadas pelo Jaeger, mostrando quais servicos chamam outros servicos.

Para uma crianca de 10 anos: e como desenhar setas entre colegas para saber quem pediu ajuda a quem durante um trabalho em grupo.

## Contrato MCP

- **Nome:** `find_dependencies`
- **Parametros:** nenhum
- **Retorno:** texto JSON formatado.

## Como funciona

1. Busca o documento de dependencias no Jaeger.
2. Le o array `data` do documento, quando presente.
3. Para cada aresta, le `parent` (quem chama) e `child` (quem recebe a chamada).
4. Remove arestas que envolvem aplicacoes desabilitadas no catalogo.
5. Retorna as arestas permitidas e a lista de aplicacoes ocultas em `disabledApplications`.

## Estrutura de resposta

```json
{
  "data": [
    {
      "parent": "ProdutoAPI",
      "child": "PrecoAPI",
      "callCount": 42
    }
  ],
  "disabledApplications": ["aplicacao-excluida"]
}
```

A tool preserva todos os campos presentes em cada aresta devolvida pelo Jaeger; `parent` e `child` sao os campos usados para aplicar o filtro.

## Exemplo de uso

```text
Use find_dependencies para descobrir quais servicos dependem de PrecoAPI e identificar impactos de uma indisponibilidade.
```

## Limitacoes

- O grafo e derivado de traces observados, portanto uma dependencia sem trafego no periodo pode nao aparecer.
- Nao explica qual rota originou a chamada. Use `find_data_origin` quando houver uma rota especifica.
- Aplicacoes desabilitadas e suas arestas sao intencionalmente removidas.
