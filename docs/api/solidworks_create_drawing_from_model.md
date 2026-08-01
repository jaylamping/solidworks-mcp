# solidworks_create_drawing_from_model

Create drawing from model.

| Field | Value |
|-------|-------|
| Worker command | `create_drawing_from_model` |
| Tier | extended |
| Read only | false |
| Destructive | true |
| Confirm required | true |
| Description source | derived |

## Parameters

| Name | Type | Required | Default | Description |
|------|------|----------|---------|-------------|
| `model_path` | string | yes | - | Value for model path. (allowed root) |
| `output_path` | string | yes | - | Value for output path. (allowed root) |
| `confirm` | const `true` | yes | `true` | Value for confirm. |

## Tags

- create

## Domains

- document

