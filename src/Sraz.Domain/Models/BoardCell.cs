namespace Sraz.Domain.Models;

public sealed class BoardCell(int row, int column, QuestionTheme theme)
{
    public int Row { get; } = row;
    public int Column { get; } = column;
    public QuestionTheme Theme { get; } = theme;
    public Pawn? Occupant { get; internal set; }
}
