{{- define "ssb.name" -}}
{{- default .Chart.Name .Values.nameOverride | trunc 63 | trimSuffix "-" -}}
{{- end -}}

{{- define "ssb.labels" -}}
app.kubernetes.io/name: {{ include "ssb.name" . }}
app.kubernetes.io/instance: {{ .Release.Name }}
app.kubernetes.io/version: {{ .Chart.AppVersion | quote }}
app.kubernetes.io/managed-by: {{ .Release.Service }}
helm.sh/chart: {{ .Chart.Name }}-{{ .Chart.Version }}
{{- end -}}

{{- define "ssb.apiImage" -}}
{{ .Values.image.registry }}/{{ .Values.image.api.repository }}:{{ .Values.image.api.tag }}
{{- end -}}

{{- define "ssb.webImage" -}}
{{ .Values.image.registry }}/{{ .Values.image.web.repository }}:{{ .Values.image.web.tag }}
{{- end -}}
