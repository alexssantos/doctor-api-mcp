{{- define "produtoapi.name" -}}
{{- default .Chart.Name .Values.nameOverride | trunc 63 | trimSuffix "-" }}
{{- end }}

{{- define "produtoapi.fullname" -}}
{{- $name := default .Chart.Name .Values.nameOverride }}
{{- printf "%s" $name | trunc 63 | trimSuffix "-" }}
{{- end }}

{{- define "produtoapi.labels" -}}
helm.sh/chart: {{ .Chart.Name }}-{{ .Chart.Version }}
app.kubernetes.io/name: {{ include "produtoapi.name" . }}
app.kubernetes.io/instance: {{ .Release.Name }}
app.kubernetes.io/version: {{ .Chart.AppVersion }}
app.kubernetes.io/managed-by: {{ .Release.Service }}
{{- end }}

{{- define "produtoapi.selectorLabels" -}}
app.kubernetes.io/name: {{ include "produtoapi.name" . }}
app.kubernetes.io/instance: {{ .Release.Name }}
{{- end }}
