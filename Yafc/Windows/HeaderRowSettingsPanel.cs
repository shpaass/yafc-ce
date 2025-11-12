using System;
using Yafc.I18n;
using Yafc.Model;
using Yafc.UI;

namespace Yafc;

public class HeaderRowSettingsPanel : PseudoScreen {
    private readonly RecipeRow? editingRow;
    private string? description;
    private FactorioObject? icon;
    private readonly Action<string?, FactorioObject?>? callback;

    private bool CanSave => !string.IsNullOrEmpty(description) || icon != null;

    private HeaderRowSettingsPanel(RecipeRow? editingRow, Action<string?, FactorioObject?>? callback) {
        this.editingRow = editingRow;
        description = editingRow?.description;
        icon = editingRow?.icon;
        this.callback = callback;
    }

    private void Build(ImGui gui, Action<FactorioObject?> setIcon) {
        _ = gui.BuildTextInput(description, out description, LSs.PageSettingsNameHint, setKeyboardFocus: editingRow == null ? SetKeyboardFocus.OnFirstPanelDraw : SetKeyboardFocus.No);
        if (gui.BuildFactorioObjectButton(icon, new ButtonDisplayStyle(4f, MilestoneDisplay.None, SchemeColor.Grey) with { UseScaleSetting = false }) == Click.Left) {
            SelectSingleObjectPanel.Select(Database.objects.all, new(LSs.SelectIcon), setIcon);
        }

        if (icon == null && gui.isBuilding) {
            gui.DrawText(gui.lastRect, LSs.PageSettingsIconHint, RectAlignment.Middle);
        }
    }

    public static void Show(RecipeRow? row, Action<string?, FactorioObject?>? callback = null) => _ = MainScreen.Instance.ShowPseudoScreen(new HeaderRowSettingsPanel(row, callback));

    public override void Build(ImGui gui) {
        gui.spacing = 3f;
        BuildHeader(gui, editingRow == null ? LSs.HeaderSettingsCreateHeader : LSs.HeaderSettingsEditHeader);
        Build(gui, s => {
            icon = s;
            Rebuild();
        });

        using (gui.EnterRow(0.5f, RectAllocator.RightRow)) {
            if (editingRow == null && gui.BuildButton(LSs.Create, active: CanSave)) {
                ReturnPressed();
            }

            if (editingRow != null && gui.BuildButton(LSs.Ok, active: CanSave)) {
                ReturnPressed();
            }

            if (gui.BuildButton(LSs.Cancel, SchemeColor.Grey)) {
                Close();
            }
        }
    }

    protected override void ReturnPressed() {
        if (!CanSave) {
            // Prevent closing with empty information
            return;
        }
        if (editingRow is null) {
            callback?.Invoke(description, icon);
        }
        else if (editingRow.description != description || editingRow.icon != icon) {
            editingRow.RecordUndo(true).description = description;
            editingRow.icon = icon;
        }
        Close();
    }
}
