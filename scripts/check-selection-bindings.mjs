import { TOOL_SPECS } from "../src/tool-spec/catalog.ts";

const selectionSpecs = TOOL_SPECS.filter((spec) => spec.selection.kind !== "none");
const bindings = selectionSpecs.filter((spec) => spec.selection.kind === "bindings");
const bindingCount = bindings.reduce(
  (count, spec) => count + spec.selection.bindings.length,
  0,
);

console.log(
  JSON.stringify(
    {
      ok: true,
      selectionAwareCommands: selectionSpecs.length,
      bindingCommands: bindings.length,
      selectionBindingCount: bindingCount,
    },
    null,
    2,
  ),
);
