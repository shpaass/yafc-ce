using System;
using System.Collections.Generic;
using System.Numerics;
using SDL2;

namespace YAFC.UI {
    public abstract class DataColumn<TData> {
        public readonly float minWidth;
        public readonly float maxWidth;
        public readonly bool isFixedSize;
        public float width;

        public DataColumn(float width, float minWidth = 0f, float maxWidth = 0f) {
            this.width = width;
            this.minWidth = minWidth == 0f ? width : minWidth;
            this.maxWidth = maxWidth == 0f ? width : maxWidth;
            isFixedSize = minWidth == maxWidth;
        }

        public abstract void BuildHeader(ImGui gui);
        public abstract void BuildElement(ImGui gui, TData data);
    }

    public abstract class TextDataColumn<TData> : DataColumn<TData> {
        public readonly string header;
        private readonly bool hasMenu;

        protected TextDataColumn(string header, float width, float minWidth = 0, float maxWidth = 0, bool hasMenu = false) : base(width, minWidth, maxWidth) {
            this.header = header;
            this.hasMenu = hasMenu;
        }
        public override void BuildHeader(ImGui gui) {
            gui.BuildText(header);
            if (hasMenu) {
                Rect rect = gui.statePosition;
                Rect menuRect = new Rect(rect.Right - 1.7f, rect.Y, 1.5f, 1.5f);
                if (gui.isBuilding) {
                    gui.DrawIcon(menuRect, Icon.DropDown, SchemeColor.BackgroundText);
                }

                if (gui.BuildButton(menuRect, SchemeColor.None, SchemeColor.Grey)) {
                    gui.ShowDropDown(menuRect, BuildMenu, new Padding(1f));
                }
            }
        }

        public virtual void BuildMenu(ImGui gui) { }
    }

    public class DataGrid<TData> where TData : class {
        public readonly List<DataColumn<TData>> columns;
        private readonly Padding innerPadding = new Padding(0.2f);
        public float width { get; private set; }
        private readonly float spacing;
        private Vector2 buildingStart;
        private ImGui contentGui;
        public float headerHeight = 1.3f;

        public DataGrid(params DataColumn<TData>[] columns) {
            this.columns = new List<DataColumn<TData>>(columns);
            spacing = innerPadding.left + innerPadding.right;
        }


        private void BuildHeaderResizer(ImGui gui, DataColumn<TData> column, Rect rect) {
            switch (gui.action) {
                case ImGuiAction.Build:
                    float center = rect.X + (rect.Width * 0.5f);
                    if (gui.IsMouseDown(rect, SDL.SDL_BUTTON_LEFT)) {
                        float unclampedWidth = gui.mousePosition.X - rect.Center.X + column.width;
                        float clampedWidth = MathUtils.Clamp(unclampedWidth, column.minWidth, column.maxWidth);
                        center = center - column.width + clampedWidth;
                    }
                    Rect viewRect = new Rect(center - 0.1f, rect.Y, 0.2f, rect.Height);
                    gui.DrawRectangle(viewRect, SchemeColor.GreyAlt);
                    break;
                case ImGuiAction.MouseMove:
                    _ = gui.ConsumeMouseOver(rect, RenderingUtils.cursorHorizontalResize);
                    if (gui.IsMouseDown(rect, SDL.SDL_BUTTON_LEFT)) {
                        gui.Rebuild();
                    }

                    break;
                case ImGuiAction.MouseDown:
                    _ = gui.ConsumeMouseDown(rect, cursor: RenderingUtils.cursorHorizontalResize);
                    break;
                case ImGuiAction.MouseUp:
                    if (gui.ConsumeMouseUp(rect, false)) {
                        float unclampedWidth = gui.mousePosition.X - rect.Center.X + column.width;
                        column.width = MathUtils.Clamp(unclampedWidth, column.minWidth, column.maxWidth);
                        contentGui?.Rebuild();
                    }
                    break;
            }
        }

        public void BuildHeader(ImGui gui) {
            float spacing = innerPadding.left + innerPadding.right;
            float x = 0f;
            Rect topSeparator = gui.AllocateRect(0f, 0.1f);
            float y = gui.statePosition.Y;
            using (ImGui.Context group = gui.EnterFixedPositioning(0f, headerHeight, innerPadding)) {
                for (int index = 0; index < columns.Count; index++) // Do not change to foreach
                {
                    DataColumn<TData> column = columns[index];
                    if (column.width < column.minWidth) {
                        column.width = column.minWidth;
                    }

                    Rect rect = new Rect(x, y, column.width, 0f);
                    @group.SetManualRectRaw(rect, RectAllocator.LeftRow);
                    column.BuildHeader(gui);
                    rect.Bottom = gui.statePosition.Y;
                    x += column.width + spacing;

                    if (!column.isFixedSize) {
                        BuildHeaderResizer(gui, column, new Rect(x - 0.7f, y, 1f, headerHeight + 0.9f));
                    }
                }
            }
            width = MathF.Max(x + 0.2f - spacing, gui.width - 1f);

            Rect separator = gui.AllocateRect(x, 0.1f);
            if (gui.isBuilding) {
                topSeparator.Width = separator.Width = width;
                gui.DrawRectangle(topSeparator, SchemeColor.GreyAlt);
                gui.DrawRectangle(separator, SchemeColor.GreyAlt);
                //DrawVerticalGrid(gui, topSeparator.Bottom, separator.Top, SchemeColor.GreyAlt);
            }
        }

        public Rect BuildRow(ImGui gui, TData element, float startX = 0f) {
            contentGui = gui;
            float x = innerPadding.left;
            SchemeColor rowColor = SchemeColor.None;
            SchemeColor textColor = rowColor;

            if (gui.ShouldBuildGroup(element, out ImGui.BuildGroup buildGroup)) {
                using (ImGui.Context group = gui.EnterFixedPositioning(width, 0f, innerPadding, textColor)) {
                    foreach (DataColumn<TData> column in columns) {
                        if (column.width < column.minWidth) {
                            column.width = column.minWidth;
                        }

                        @group.SetManualRect(new Rect(x, 0, column.width, 0f), RectAllocator.LeftRow);
                        column.BuildElement(gui, element);
                        x += column.width + spacing;
                    }
                }
                buildGroup.Complete();
            }

            Rect rect = gui.lastRect;
            float bottom = gui.lastRect.Bottom;
            if (gui.isBuilding) {
                gui.DrawRectangle(new Rect(startX, bottom - 0.1f, width - startX, 0.1f), SchemeColor.Grey);
            }

            return rect;
        }

        public void BeginBuildingContent(ImGui gui) {
            buildingStart = gui.statePosition.BottomLeft;
            gui.spacing = innerPadding.top + innerPadding.bottom;
        }

        public Rect EndBuildingContent(ImGui gui) {
            float bottom = gui.statePosition.Bottom;
            return new Rect(buildingStart.X, buildingStart.Y, width, bottom - buildingStart.Y);
        }

        public bool BuildContent(ImGui gui, IReadOnlyList<TData> data, out (TData from, TData to) reorder, out Rect rect, Func<TData, bool> filter = null) {
            BeginBuildingContent(gui);
            reorder = default;
            bool hasReorder = false;
            for (int i = 0; i < data.Count; i++) // do not change to foreach
            {
                TData t = data[i];
                if (filter != null && !filter(t)) {
                    continue;
                }

                Rect rowRect = BuildRow(gui, t);
                if (!hasReorder && gui.DoListReordering(rowRect, rowRect, t, out TData from, SchemeColor.PureBackground, false)) {
                    reorder = (@from, t);
                    hasReorder = true;
                }
            }

            rect = EndBuildingContent(gui);
            return hasReorder;
        }
    }
}
