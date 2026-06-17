using System.Text.Json;

internal static class CommandSafety
{
    private static readonly HashSet<string> DestructiveCommands = new(StringComparer.Ordinal)
    {
        "delete_all_mates",
        "delete_mates_in_range",
        "delete_mate",
        "close_all_documents",
        "torso_frame_build_mates",
        "layout_add_shoulder_mounts",
        "place_shoulder_roll_motors",
        "build_torso_compute_shelf",
        "cut_actuator_cavity",
        "apply_shoulder_roll_golden",
        "vendor_add_rs03_urdf_frame",
        "clone_solid_body_part",
        "mirror_part_file",
        "make_component_independent",
        "replace_components_by_path",
        "replace_component_path",
        "create_mallet_mount",
        "delete_feature",
        "dissolve_component",
        "mirror_component",
        "feature_extrude_boss",
        "sketch_rectangle",
        "create_sketch",
        "new_document",
        "actuator_mount_hole_pattern",
        "actuator_cut_cavity",
        "actuator_add_urdf_frame",
        "actuator_insert_vendor",
        "feature_extrude_cut",
        "feature_fillet",
        "feature_chamfer",
        "feature_mirror",
        "feature_linear_pattern",
        "feature_circular_pattern",
        "set_material",
        "create_subassembly",
        "explode_view",
        "copy_with_mates",
        "create_drawing_from_model",
    };

    public static bool IsDestructive(string command) => DestructiveCommands.Contains(command);

    public static void RequireConfirmIfDestructive(string command, JsonElement? args)
    {
        if (!IsDestructive(command))
        {
            return;
        }

        bool confirmed = false;
        if (args is not null && args.Value.ValueKind == JsonValueKind.Object
            && args.Value.TryGetProperty("confirm", out JsonElement confirmValue))
        {
            confirmed = confirmValue.ValueKind == JsonValueKind.True;
        }

        if (!confirmed)
        {
            throw WorkerException.Validation(
                "CONFIRM_REQUIRED",
                $"Destructive command '{command}' requires confirm: true in args.",
                new Dictionary<string, object?> { ["command"] = command },
                [
                    "Re-run with confirm: true only when the user explicitly requested this destructive operation.",
                    "Consider solidworks_checkpoint_document first to preserve a rollback copy.",
                ]);
        }
    }
}
