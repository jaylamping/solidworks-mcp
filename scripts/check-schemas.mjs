import { TOOL_SPECS } from "../src/tool-spec/catalog.ts";

function schemaKeys(schema) {
  const definition = schema?._def;
  const shape = typeof definition?.shape === "function" ? definition.shape() : definition?.shape;
  return shape && typeof shape === "object" ? new Set(Object.keys(shape)) : null;
}

const errors = [];
const seenCommands = new Set();
const seenNames = new Set();
const schemaValues = new Map();
const mcpSpecs = TOOL_SPECS.filter((spec) => spec.exposure.kind === "mcp");

for (const spec of TOOL_SPECS) {
  const command = spec.implementation.command;
  if (seenCommands.has(command)) {
    errors.push(`duplicate catalog command: ${command}`);
  }
  seenCommands.add(command);

  if (spec.exposure.kind !== "mcp") {
    continue;
  }

  if (seenNames.has(spec.exposure.name)) {
    errors.push(`duplicate MCP tool name: ${spec.exposure.name}`);
  }
  seenNames.add(spec.exposure.name);

  const schema = spec.exposure.input;
  if (!schema?.key || !schema?.value) {
    errors.push(`MCP tool has no schemaRef: ${command}`);
    continue;
  }

  const previous = schemaValues.get(schema.key);
  if (previous && previous !== schema.value) {
    errors.push(`schema key is paired with multiple Zod objects: ${schema.key}`);
  }
  schemaValues.set(schema.key, schema.value);

  const keys = schemaKeys(schema.value);
  if (keys && spec.selection.kind === "bindings") {
    for (const binding of spec.selection.bindings) {
      if (!keys.has(binding.targetArg)) {
        errors.push(
          `selection target ${binding.targetArg} is absent from ${schema.key}: ${command}`,
        );
      }
    }
  }

  if (spec.safety.kind !== "read" && spec.safety.destructive && schema.key === "optionalPath") {
    errors.push(`destructive command ${command} must not use optionalPath schema`);
  }
}

if (errors.length) {
  console.error("Schema coverage check failed:\n");
  for (const error of errors) {
    console.error(`- ${error}`);
  }
  process.exit(1);
}

console.log(
  JSON.stringify(
    {
      ok: true,
      catalogCommands: TOOL_SPECS.length,
      mcpTools: mcpSpecs.length,
      schemaKeys: schemaValues.size,
      destructiveMapped: mcpSpecs.filter(
        (spec) => spec.safety.kind !== "read" && spec.safety.destructive,
      ).length,
    },
    null,
    2,
  ),
);
