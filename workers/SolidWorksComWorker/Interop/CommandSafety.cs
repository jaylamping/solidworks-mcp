using System.Text.Json;

internal static class CommandSafety
{
    private static readonly HashSet<string> DestructiveCommands = new(StringComparer.Ordinal)
    {
        "delete_all_mates",
        "delete_mates_in_range",
        "delete_mate",
        "close_all_documents",
        "add_urdf_frame",
        "clone_solid_body_part",
        "mirror_part_file",
        "make_component_independent",
        "replace_components_by_path",
        "replace_component_path",
        "delete_feature",
        "dissolve_component",
        "mirror_component",
        "feature_extrude_boss",
        "sketch_rectangle",
        "create_sketch",
        "new_document",
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
        "restore_from_checkpoint",
        "confirm_and_save",
    };

    /// <summary>
    /// Commands that change model/assembly state and must stage a rollback copy first.
    /// Meta/IO/selection/read-only commands are intentionally excluded.
    /// </summary>
    private static readonly HashSet<string> AutoCheckpointCommands = new(StringComparer.Ordinal)
    {
        "add_configuration_copy",
        "add_standard_views",
        "add_urdf_frame",
        "align_component_to_feature",
        "clone_solid_body_part",
        "confirm_and_save",
        "copy_with_mates",
        "create_drawing_from_model",
        "create_sketch",
        "create_subassembly",
        "delete_all_mates",
        "delete_feature",
        "delete_mate",
        "delete_mates_in_range",
        "dissolve_component",
        "ensure_offset_plane",
        "explode_view",
        "feature_chamfer",
        "feature_circular_pattern",
        "feature_extrude_boss",
        "feature_extrude_cut",
        "feature_fillet",
        "feature_linear_pattern",
        "feature_mirror",
        "insert_component",
        "insert_coord_sys",
        "make_component_independent",
        "mate_coincident",
        "mate_component_origin",
        "mate_coord_sys",
        "mate_distance",
        "mate_limit_angle",
        "mate_parallel",
        "mate_perpendicular",
        "mate_planes",
        "mate_replay_sequence",
        "mate_tangent",
        "mate_width",
        "mirror_component",
        "mirror_part_file",
        "rebuild_document",
        "rename_component",
        "replace_component_path",
        "replace_components_by_path",
        "reset_component_transform",
        "resolve_lightweight",
        "round_side_arms_from_circle",
        "set_component_configuration",
        "set_component_fixed",
        "set_component_transform",
        "set_custom_properties",
        "set_dimension",
        "set_feature_suppression",
        "set_mate_limit_angle",
        "set_mate_suppression",
        "set_material",
        "sketch_circle",
        "sketch_exit",
        "sketch_line",
        "sketch_rectangle",
        "transform_component",
        "unfix_all_components",
    };

    public static bool IsDestructive(string command) => DestructiveCommands.Contains(command);

    public static bool ShouldAutoCheckpoint(string command)
    {
        if (!AutoCheckpointEnabled())
        {
            return false;
        }

        return AutoCheckpointCommands.Contains(command);
    }

    public static bool AutoCheckpointEnabled()
    {
        string? raw = Environment.GetEnvironmentVariable("SOLIDWORKS_MCP_AUTO_CHECKPOINT");
        if (string.IsNullOrWhiteSpace(raw))
        {
            return true;
        }

        return raw is not "0" and not "false" and not "False" and not "FALSE" and not "no" and not "off";
    }

    public static int AutoCheckpointDebounceSeconds()
    {
        string? raw = Environment.GetEnvironmentVariable("SOLIDWORKS_MCP_CHECKPOINT_DEBOUNCE_SEC");
        if (int.TryParse(raw, out int seconds) && seconds >= 0)
        {
            return seconds;
        }

        return 45;
    }

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
                    "A pre-change checkpoint is staged automatically for mutating commands when possible.",
                ]);
        }
    }
}
