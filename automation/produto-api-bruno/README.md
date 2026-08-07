# Simulacao de atividade da ProdutoAPI

Colecao Bruno que executa, em sequencia, a criacao, consulta, atualizacao, listagem e remocao de um produto. O executor repete a colecao por 5 minutos e aguarda 8 segundos entre rodadas para permanecer abaixo do limite global de 100 requests por minuto da API.

## Pre-requisito

Instale o Bruno CLI uma vez:

```powershell
npm install --global @usebruno/cli
```

## Execucao

Com a ProdutoAPI acessivel, execute a partir desta pasta:

```powershell
.\Invoke-ProdutoApiActivity.ps1
```

O alvo padrao e `http://localhost:5002`. Para usar o ingresso Kubernetes ou alterar o intervalo:

```powershell
.\Invoke-ProdutoApiActivity.ps1 -BaseUrl http://produtoapi.local:8080 -IntervalSeconds 8
```

Para uma verificacao curta da colecao:

```powershell
.\Invoke-ProdutoApiActivity.ps1 -DurationSeconds 10
```

Cada rodada remove o produto que criou. Caso uma rodada falhe, o script para imediatamente e o Bruno informa a request e a assercao que falharam.