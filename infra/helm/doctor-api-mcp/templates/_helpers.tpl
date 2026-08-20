{{- define "doctor-api-mcp.name" -}}
{{- default .Chart.Name .Values.nameOverride | trunc 63 | trimSuffix "-" }}
{{- end }}
{{- define "doctor-api-mcp.fullname" -}}
{{- if .Values.fullnameOverride }}
{{- .Values.fullnameOverride | trunc 63 | trimSuffix "-" }}
{{- else }}
{{- $name := default .Chart.Name .Values.nameOverride }}
{{- if contains $name .Release.Name }}
{{- .Release.Name | trunc 63 | trimSuffix "-" }}
{{- else }}
{{- printf "%s-%s" .Release.Name $name | trunc 63 | trimSuffix "-" }}
{{- end }}
{{- end }}
{{- end }}

{{- define "doctor-api-mcp.labels" -}}
helm.sh/chart: {{ printf "%s-%s" .Chart.Name .Chart.Version | replace "+" "_" }}
app.kubernetes.io/name: {{ include "doctor-api-mcp.name" . }}
app.kubernetes.io/instance: {{ .Release.Name }}
app.kubernetes.io/version: {{ .Chart.AppVersion | quote }}
app.kubernetes.io/managed-by: {{ .Release.Service }}
{{- end }}

{{- define "doctor-api-mcp.selectorLabels" -}}
app.kubernetes.io/name: {{ include "doctor-api-mcp.name" . }}
app.kubernetes.io/instance: {{ .Release.Name }}
{{- end }}

{{- define "doctor-api-mcp.serviceAccountName" -}}
{{- if .Values.serviceAccount.create }}
{{- default (include "doctor-api-mcp.fullname" .) .Values.serviceAccount.name }}
{{- else }}
{{- default "default" .Values.serviceAccount.name }}
{{- end }}
{{- end }}

{{- define "doctor-api-mcp.authSecretName" -}}
{{- default (printf "%s-auth" (include "doctor-api-mcp.fullname" .)) .Values.security.authentication.existingSecret }}
{{- end }}

{{/* Fail before rendering an installation whose declared restrictions cannot work. */}}
{{- define "doctor-api-mcp.validateValues" -}}
{{- $scope := lower (toString .Values.clusterAccess.scope) -}}
{{- $state := lower (toString .Values.clusterAccess.stateStorage) -}}
{{- if not (has $scope (list "cluster" "namespace" "none")) -}}
{{- fail "clusterAccess.scope must be Cluster, Namespace or None" -}}
{{- end -}}
{{- if not (has $state (list "configmap" "memory")) -}}
{{- fail "clusterAccess.stateStorage must be ConfigMap or Memory" -}}
{{- end -}}
{{- if and (eq $scope "none") .Values.clusterAccess.serviceDiscovery -}}
{{- fail "clusterAccess.serviceDiscovery must be false when clusterAccess.scope is None" -}}
{{- end -}}
{{- if and (eq $scope "none") (ne $state "memory") -}}
{{- fail "clusterAccess.stateStorage must be Memory when clusterAccess.scope is None" -}}
{{- end -}}
{{- if and (eq $scope "none") .Values.observability.enableDeploymentEvents -}}
{{- fail "observability.enableDeploymentEvents must be false when clusterAccess.scope is None" -}}
{{- end -}}
{{- if and (eq $scope "none") (ne .Values.serviceAccount.automountToken nil) .Values.serviceAccount.automountToken -}}
{{- fail "serviceAccount.automountToken cannot be true when clusterAccess.scope is None" -}}
{{- end -}}
{{- if and (eq $state "memory") (gt (int .Values.replicaCount) 1) -}}
{{- fail "replicaCount must be 1 when clusterAccess.stateStorage is Memory" -}}
{{- end -}}
{{- if and (not .Values.clusterAccess.serviceDiscovery) (eq (len .Values.services) 0) -}}
{{- fail "at least one services entry is required when clusterAccess.serviceDiscovery is false" -}}
{{- end -}}
{{- if eq $scope "namespace" -}}
  {{- $allowed := .Values.security.allowedNamespaces | default (list .Release.Namespace) -}}
  {{- if ne (len $allowed) 1 -}}
  {{- fail "Namespace scope requires exactly one security.allowedNamespaces entry" -}}
  {{- end -}}
  {{- if ne (index $allowed 0) .Release.Namespace -}}
  {{- fail "Namespace scope requires security.allowedNamespaces to match the Helm release namespace" -}}
  {{- end -}}
{{- end -}}
{{- end -}}
