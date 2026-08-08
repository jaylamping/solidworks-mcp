# solidworks_checkpoint_document

Save a recoverable checkpoint of the active or specified document.

Save a recoverable snapshot under `<document-dir>/.checkpoints/` before risky edits.

| Field | Value |
|-------|-------|
| Worker command | `checkpoint_document` |
| Tier | core |
| Read only | false |
| Destructive | false |
| Confirm required | false |
| Description source | authored |

## Parameters

| Name | Type | Required | Default | Description |
|------|------|----------|---------|-------------|
| `path` | string | yes | - | Value for path. (allowed root) |

## Args

| Name | Type | Required | Notes |
|------|------|----------|-------|
| `path` | string | yes | Assembly/part to stage |
| `force` | boolean | no | Bypass debounce and create a fresh snapshot (default true for explicit calls) |

## Behavior

1. If the document is open in SolidWorks, prefer `SaveAs` with **Copy** so unsaved session state is captured without renaming the working doc.
2. Otherwise copy the on-disk file into `.checkpoints/{name}_{yyyyMMdd}_{HHmmss}{ext}`.
3. Mutating tools also call this automatically and return `preCheckpoint` on their result.

## Tags

- checkpoint

## Domains

- document
