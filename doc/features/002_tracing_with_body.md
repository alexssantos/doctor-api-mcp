# Capturar body completo para tracing com openTelemtry e jeager. 

Por padrão, o Jaeger (via OpenTelemetry) **não captura body** por motivos de performance e segurança.

👉 Então você precisa **enriquecer manualmente os spans**.

Vou te mostrar o jeito **correto (e seguro)** no seu cenário (.NET 10 + ProdutoAPI → PrecoAPI).

---

# ⚠️ Primeiro: o trade-off real

Capturar payload completo tem custo:

* 🔴 Pode expor dados sensíveis
* 🔴 Aumenta latência/memória
* 🔴 Pode quebrar LGPD dependendo do conteúdo

👉 Regra prática:

> Capture body **apenas em debug, sampling ou ambientes controlados**

---

# ✅ Estratégia correta

Você precisa de 2 coisas:

1. Interceptar request/response no ASP.NET
2. Adicionar isso como **tags no span atual**

---

# 🧩 1. Capturar REQUEST BODY

Crie um middleware:

```csharp
public class RequestBodyTelemetryMiddleware
{
    private readonly RequestDelegate _next;

    public RequestBodyTelemetryMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task Invoke(HttpContext context)
    {
        context.Request.EnableBuffering();

        var body = await new StreamReader(context.Request.Body).ReadToEndAsync();

        context.Request.Body.Position = 0;

        var activity = Activity.Current;
        if (activity != null && !string.IsNullOrEmpty(body))
        {
            activity.SetTag("http.request.body", Truncate(body));
        }

        await _next(context);
    }

    private string Truncate(string input)
        => input.Length > 1000 ? input.Substring(0, 1000) : input;
}
```

---

# 🧩 2. Capturar RESPONSE BODY

```csharp
public class ResponseBodyTelemetryMiddleware
{
    private readonly RequestDelegate _next;

    public ResponseBodyTelemetryMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task Invoke(HttpContext context)
    {
        var originalBody = context.Response.Body;

        using var memStream = new MemoryStream();
        context.Response.Body = memStream;

        await _next(context);

        memStream.Position = 0;
        var responseBody = await new StreamReader(memStream).ReadToEndAsync();

        var activity = Activity.Current;
        if (activity != null && !string.IsNullOrEmpty(responseBody))
        {
            activity.SetTag("http.response.body", Truncate(responseBody));
        }

        memStream.Position = 0;
        await memStream.CopyToAsync(originalBody);
    }

    private string Truncate(string input)
        => input.Length > 1000 ? input.Substring(0, 1000) : input;
}
```

---

# 🧩 3. Registrar no pipeline

```csharp
app.UseMiddleware<RequestBodyTelemetryMiddleware>();
app.UseMiddleware<ResponseBodyTelemetryMiddleware>();
```

👉 Faça isso em:

* ProdutoAPI
* PrecoAPI

---

# 🌐 4. Capturar chamadas HTTP (Produto → Preço)

Agora o mais importante no seu cenário.

## 👉 Handler no HttpClient

```csharp
public class HttpClientTelemetryHandler : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var activity = Activity.Current;

        if (request.Content != null)
        {
            var requestBody = await request.Content.ReadAsStringAsync();
            activity?.SetTag("http.client.request.body", requestBody);
        }

        var response = await base.SendAsync(request, cancellationToken);

        if (response.Content != null)
        {
            var responseBody = await response.Content.ReadAsStringAsync();
            activity?.SetTag("http.client.response.body", responseBody);
        }

        return response;
    }
}
```

---

## 👉 Registrar no HttpClient

```csharp
services.AddHttpClient<PriceClient>()
    .AddHttpMessageHandler<HttpClientTelemetryHandler>();
```

---

# 🔍 Resultado no Jaeger

![Image](https://images.openai.com/static-rsc-4/FCwv-QrFZWTmXdxyS0l--WRxO4GtF5NpnU4ul1VsNg-1pPWYWGfYgjNWY897z5VP3m364sJ4rvVF77lgdpRmaLOC2BFjqPpD50LBQ70qLtgsdceYwEOJG-_o4N-y-vkIVSd5ZKt87t9bE2gTd-p3ahHKjbbX6WfDHWk52_x4tKQQI2hyi8baBY6rbiwyAZTU?purpose=fullsize)

![Image](https://images.openai.com/static-rsc-4/Xynfcv2gK5f7hBpVI-cAAnn5zr92_vgQxt96t9tYIB0gnpDHk9go7u1Vw0Lb3uGGmnA8BDCV0_h-naxxY_q8WUWVwPI79yV4BRZfxcWaLWGbIDSARkQ2Wj9O_pDFsuh6M6sPMUL_baWRhtNbKNnS93h8BcrUbFap9Fjck3Iav2rv6bNvPbE0gXIFb0hozBU_?purpose=fullsize)

![Image](https://images.openai.com/static-rsc-4/REHK5jFdxWEcPZgAgEKuRIPjRJ-krzZ-SMFIVKfag-3-2A4xe1EZkyzTOyxdeN7fksZg_1ZuUAyN4X6AIo1EkqA1eUCN5vHajZPsNKlNm-62emVgteW5O01bjz938JbOFCljy_MVE0FklwSV03hSEP25S_nx6HEzFgj7j_oLFdmd_KRh4ATsyJmi7vXV07hr?purpose=fullsize)

![Image](https://images.openai.com/static-rsc-4/nxn-AUzpv1EfjuOAJLQAtDhZ8pjCX47NGEUff9njPNcHs0FBZ5wlTONTWhJFVX1dTtpQHgZR-jSZY51wlubOVhf70WDhZAN87shkNszUtjec3qON_EDinTYnIgnr_FFnsJVzhjp-3yMalnqZlmlHASNA76t6SZYhU7vpZ6AuKzKQjx_avYRKXZloMFsQCWr_?purpose=fullsize)

![Image](https://images.openai.com/static-rsc-4/6eBdkfSwb9c3Bmsf-J_u94JykDgQJpkJpXT281BsYSE6qp2Rz5xwAWnj5cpLcVNZ8WBb80jZ2VChqtQ4rfPF2L-CoBv_XqgjK9WUq27AXobJfsP1IeDjtsRe0QibofAqVMuzUEhbyj9yJEgYfyUl6XVJTVvB9lGn6W7vlB_X8Uyld5HBSJtTEQGXieGNsKjn?purpose=fullsize)

![Image](https://images.openai.com/static-rsc-4/T9hLD0QUKy8SgVqjvRsGgdPyvK8Jl26ktcVC0_pHC-d_aNNPw7gVbt58fTQgidDisS3yrdjhwOVkpded65LT57ooSScOOLWKyji4C1Knmg1od1shvbLMuxeeZSyvusEgr-HpMTg4Gvvj4pGxiK3oFAh1AFlc09v4LPpJNRT9oQKfJ8Bz6EqeP09l8lfXDyoM?purpose=fullsize)

Você verá no trace:

```
Span: HTTP GET /api/products
  ├── http.request.body
  ├── http.response.body
  └── child span:
        HTTP GET /api/prices/{id}
          ├── http.client.request.body
          └── http.client.response.body
```

👉 Tudo correlacionado automaticamente

---

# 🧠 Insight avançado

👉 Em produção madura, normalmente você NÃO usa Jaeger para payload completo.

Você usa:

* tracing → fluxo (Jaeger)
* logging estruturado → payload (ex: Elastic Stack)

obs: utilizar env de producao para ignorar trace com body. 

---

# 🔥 TL;DR

Para ver payload no Jaeger:

1. Middleware para request/response
2. DelegatingHandler para HttpClient
3. Adicionar como `Activity.SetTag`
4. Limitar/sanitizar
