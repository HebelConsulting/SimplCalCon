{{- define "simplcalcon.name" -}}
{{- default .Chart.Name .Values.nameOverride | trunc 63 | trimSuffix "-" -}}
{{- end -}}

{{- define "simplcalcon.fullname" -}}
{{- printf "%s-%s" .Release.Name (include "simplcalcon.name" .) | trunc 63 | trimSuffix "-" -}}
{{- end -}}

{{- define "simplcalcon.labels" -}}
app.kubernetes.io/name: {{ include "simplcalcon.name" . }}
app.kubernetes.io/instance: {{ .Release.Name }}
app.kubernetes.io/version: {{ .Chart.AppVersion | quote }}
app.kubernetes.io/managed-by: {{ .Release.Service }}
helm.sh/chart: {{ printf "%s-%s" .Chart.Name .Chart.Version }}
{{- end -}}

{{- define "simplcalcon.selectorLabels" -}}
app.kubernetes.io/name: {{ include "simplcalcon.name" . }}
app.kubernetes.io/instance: {{ .Release.Name }}
{{- end -}}

{{- define "simplcalcon.secretName" -}}
{{- if .Values.database.existingSecretName -}}
{{- .Values.database.existingSecretName -}}
{{- else -}}
{{- printf "%s-config" (include "simplcalcon.fullname" .) -}}
{{- end -}}
{{- end -}}
