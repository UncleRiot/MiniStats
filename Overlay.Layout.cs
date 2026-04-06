using System;
using System.Drawing;

namespace MiniStats
{
    public sealed partial class Overlay
    {
        private const int OverlayWidth = 260;
        private const int OuterPadding = 10;
        private const int RowHeight = 28;
        private const int LabelWidth = 58;

        private int boxWidth = 170;
        private int boxSpacing = 8;

        private void UpdateOverlayBounds()
        {
            int visibleRowCount = GetVisibleRowCount();

            if (visibleRowCount <= 0)
            {
                Width = OuterPadding * 2;
                Height = OuterPadding * 2;
                return;
            }

            if (displayHorizontal)
            {
                Width = (OuterPadding * 2) + (boxWidth * visibleRowCount) + (boxSpacing * (visibleRowCount - 1));
                Height = (OuterPadding * 2) + RowHeight;
                return;
            }

            Width = boxWidth + (OuterPadding * 2);
            Height = (OuterPadding * 2) + (RowHeight * visibleRowCount) + (boxSpacing * (visibleRowCount - 1));
        }

        private Font CreateLabelFont()
        {
            return new Font("Segoe UI", Math.Max(9, fontSize - 4), FontStyle.Bold, GraphicsUnit.Pixel);
        }

        private Font CreateValueFont()
        {
            return new Font("Segoe UI", fontSize, FontStyle.Regular, GraphicsUnit.Pixel);
        }

        private void RenderDefaultLayout(Graphics graphics)
        {
            using Font labelFont = CreateLabelFont();
            using Font valueFont = CreateValueFont();

            int backgroundAlpha = (int)Math.Round(255.0 * backgroundOpacityPercent / 100.0);

            using Brush labelBrush = new SolidBrush(Color.FromArgb(255, 255, 170, 0));
            using Brush fpsValueBrush = new SolidBrush(Color.White);
            using Brush cpuValueBrush = new SolidBrush(cpuOverlayValueColor);
            using Brush gpuValueBrush = new SolidBrush(gpuOverlayValueColor);
            using Brush rowBrush = new SolidBrush(Color.FromArgb(backgroundAlpha, 24, 24, 24));
            using Pen borderPen = new Pen(Color.FromArgb(backgroundAlpha, 50, 50, 50));

            int rowIndex = 0;

            if (showFps)
            {
                DrawRow(graphics, rowBrush, borderPen, labelBrush, fpsValueBrush, labelFont, valueFont, rowIndex, "FPS", fpsText);
                rowIndex++;
            }

            if (showCpu)
            {
                DrawRow(graphics, rowBrush, borderPen, labelBrush, cpuValueBrush, labelFont, valueFont, rowIndex, "CPU", cpuTemperatureText);
                rowIndex++;
            }

            if (showGpu)
            {
                DrawRow(graphics, rowBrush, borderPen, labelBrush, gpuValueBrush, labelFont, valueFont, rowIndex, "GPU", gpuTemperatureText);
            }
        }

        private void DrawRow(
            Graphics graphics,
            Brush rowBrush,
            Pen borderPen,
            Brush labelBrush,
            Brush valueBrush,
            Font labelFont,
            Font valueFont,
            int rowIndex,
            string label,
            string value)
        {
            Rectangle rect;

            if (displayHorizontal)
            {
                int left = OuterPadding + (rowIndex * (boxWidth + boxSpacing));
                rect = new Rectangle(left, OuterPadding, boxWidth, RowHeight);
            }
            else
            {
                int top = OuterPadding + (rowIndex * (RowHeight + boxSpacing));
                rect = new Rectangle(OuterPadding, top, boxWidth, RowHeight);
            }

            graphics.FillRectangle(rowBrush, rect);
            graphics.DrawRectangle(borderPen, rect);

            int labelPaddingLeft = 8;
            int innerSpacing = 6;
            int valuePaddingRight = 8;

            int measuredLabelWidth = (int)Math.Ceiling(graphics.MeasureString(label, labelFont).Width);
            int effectiveLabelWidth = Math.Max(28, Math.Min(measuredLabelWidth, rect.Width - 30));

            Rectangle labelRect = new Rectangle(rect.Left + labelPaddingLeft, rect.Top, effectiveLabelWidth, rect.Height);
            Rectangle valueRect = new Rectangle(
                labelRect.Right + innerSpacing,
                rect.Top,
                Math.Max(0, rect.Right - valuePaddingRight - (labelRect.Right + innerSpacing)),
                rect.Height);

            using StringFormat labelFormat = new StringFormat
            {
                Alignment = StringAlignment.Near,
                LineAlignment = StringAlignment.Center,
                FormatFlags = StringFormatFlags.NoWrap
            };

            using StringFormat valueFormat = new StringFormat
            {
                Alignment = StringAlignment.Near,
                LineAlignment = StringAlignment.Center,
                FormatFlags = StringFormatFlags.NoWrap,
                Trimming = StringTrimming.EllipsisCharacter
            };

            graphics.DrawString(label, labelFont, labelBrush, labelRect, labelFormat);
            graphics.DrawString(value, valueFont, valueBrush, valueRect, valueFormat);
        }

        private int GetVisibleRowCount()
        {
            int visibleRowCount = 0;

            if (showFps)
            {
                visibleRowCount++;
            }

            if (showCpu)
            {
                visibleRowCount++;
            }

            if (showGpu)
            {
                visibleRowCount++;
            }

            return visibleRowCount;
        }
    }
}