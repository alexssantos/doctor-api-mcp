# AKS release overlay

This overlay is deliberately a release template, not a deployable default. It
inherits the hardened workload/RBAC/NetworkPolicy and changes the runtime to
three replicas, authenticated APIs, a pullable registry image and a Gateway API
`HTTPRoute`. It never inherits the k3d `*.local` hosts or its Traefik Ingress.

Before rendering a release:

1. Set `images[0].newName` and `newTag` to the authorized registry image and an
   immutable release tag or digest.
2. Replace every `example.invalid` DNS name with the approved public FQDNs.
3. Replace the four `observability.svc.cluster.local` endpoints with the target
   platform's actual Prometheus, Jaeger, Loki and OTLP services.
4. Provision `Secret/mcpserver-auth` out of band with `reader-api-key` and
   `admin-api-key`; do not commit either value. External Secrets, Azure Key
   Vault CSI or an equivalent managed path is expected.
5. Provision a supported Gateway API controller and HTTPS listener. Replace the
   `platform-gateway`, `gateway-system` and `https` parent reference in
   `httproute.yaml`; the platform-owned Gateway is responsible for DNS, TLS and
   certificate lifecycle. Traefik can be selected here only when it is the
   platform-approved and supported Gateway API implementation.
6. Configure cookie session persistence for `HTTPRoute/mcpserver` using the
   selected controller's supported policy, then prove that a Streamable HTTP
   MCP session stays on one replica. Application Gateway for Containers, for
   example, exposes this through an `alb.networking.azure.io/v1` `RoutePolicy`.
7. Label only the Gateway controller namespace with
   `ingress.mcp-apis.io/allow=true`; the AKS NetworkPolicy rejects other ingress
   namespaces.
8. Label every observed namespace with
   `observability.mcp-apis.io/allow=true`, add it to
   `Security__AllowedNamespaces`, and revalidate the egress/RBAC matrix.

The release gate must fail if any placeholder remains:

```bash
kubectl kustomize infra/k8s/overlays/aks > /tmp/mcpserver-aks.yaml
if grep -ER 'example\.invalid|newTag:[[:space:]]+release|registry\.example\.invalid|platform-gateway|gateway-system' \
  infra/k8s/overlays/aks; then
  echo 'AKS release placeholders remain' >&2
  exit 1
fi
kubectl api-resources --api-group=gateway.networking.k8s.io
kubectl apply --dry-run=server -f /tmp/mcpserver-aks.yaml
kubectl get gateway,httproute -A
```

After applying, require `Accepted=True`, `ResolvedRefs=True` and a programmed
parent Gateway before exercising TLS, affinity, authentication and resilience.

Image publication, DNS, certificates, secret creation, the controller-specific
affinity policy and a real AKS context require platform credentials and cannot
be validated in k3d.
