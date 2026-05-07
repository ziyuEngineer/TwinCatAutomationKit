# Step Record Template / Step 记录模板

English summary: Use this template when a public step gains direct CLI support or receives real-machine validation.

这个模板用于记录一个 public step 的身份、前置条件、预期效果、证据和失败模式。它的价值在于趁信息新鲜时写清楚，以后写 usage guide 或排查真实机器问题时不用重新猜。

```md
# Step Record: <step kind>

## Identity / 身份

- Step kind:
- Human TwinCAT action:
- Public C# method:
- Direct CLI command:

## Preconditions / 前置条件

- Machine prerequisites:
- Required prior steps:
- Inputs that must already exist:

## Expected Effect / 预期效果

- Expected visible change in XAE:
- Expected on-disk change:
- Expected runtime change:

## Evidence / 证据

- Request preview or payload file:
- XML snapshot:
- Summary JSON:
- Build or activation output:
- ADS readback:

## Validation / 验证

- Integration test coverage:
- Real-machine validation command:
- Real-machine validation result:

## Failure Modes / 失败模式

- Common failure:
- How to detect it:
- How to recover:

## Usage Guide Notes / 使用说明要点

- Best minimal example:
- Warnings to mention:
- Good screenshot or artifact candidate:
```
