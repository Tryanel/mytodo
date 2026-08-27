using System.Windows;

namespace PaperTodo.Tests;

public sealed class TodoNoteDialogPlacementTests
{
    [Fact]
    public void Placement_stays_in_the_owners_negative_coordinate_work_area()
    {
        var placement = TodoNoteDialogPlacement.Calculate(
            new Rect(-1720, 120, 560, 360),
            new Size(460, 390),
            new Rect(-1920, 0, 1920, 1040));

        Assert.Equal(-1670, placement.X);
        Assert.Equal(120, placement.Y);
        Assert.InRange(placement.X, -1920, -460);
        Assert.InRange(placement.Y, 0, 650);
    }

    [Fact]
    public void Oversized_dialog_anchors_to_the_target_work_area_origin()
    {
        var placement = TodoNoteDialogPlacement.Calculate(
            new Rect(2100, 100, 400, 300),
            new Size(1400, 1000),
            new Rect(1920, 0, 1200, 900));

        Assert.Equal(new Point(1920, 0), placement);
    }
}
