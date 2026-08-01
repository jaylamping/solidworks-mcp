# solidworks_new_document

Create document.

| Field | Value |
|-------|-------|
| Worker command | `new_document` |
| Tier | extended |
| Read only | false |
| Destructive | true |
| Confirm required | true |
| Description source | derived |

## Parameters

| Name | Type | Required | Default | Description |
|------|------|----------|---------|-------------|
| `doc_type` | `part`, `assembly`, `drawing` | no | - | Value for doc type. |
| `output_path` | string | no | - | Value for output path. (allowed root) |
| `confirm` | const `true` | yes | `true` | Value for confirm. |

## Tags

- new

## Domains

- document

