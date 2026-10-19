namespace Sraz.Domain.Models;

public sealed class Board
{
    public const int DefaultSize = 10;

    public Board(int size, IReadOnlyList<QuestionTheme> themes)
    {
        if (size <= 5)
        {
            throw new ArgumentOutOfRangeException(nameof(size), "Board size must be greater than 5.");
        }

        if (themes.Count != size * size)
        {
            throw new ArgumentException("A theme must be provided for every board cell.", nameof(themes));
        }

        Size = size;
        Cells = new BoardCell[size, size];
        for (var row = 0; row < size; row++)
        {
            for (var column = 0; column < size; column++)
            {
                Cells[row, column] = new BoardCell(row, column, themes[row * size + column]);
            }
        }
    }

    public int Size { get; }
    public BoardCell[,] Cells { get; }

    public BoardCell GetCell(int row, int column)
    {
        if (row < 0 || row >= Size || column < 0 || column >= Size)
        {
            throw new ArgumentOutOfRangeException(nameof(row));
        }

        return Cells[row, column];
    }

    public void PlacePawn(Pawn pawn, int row, int column)
    {
        ArgumentNullException.ThrowIfNull(pawn);
        var cell = GetCell(row, column);
        if (cell.Occupant is not null)
        {
            throw new InvalidOperationException("The board cell is already occupied.");
        }

        pawn.Row = row;
        pawn.Column = column;
        cell.Occupant = pawn;
    }

    public void MovePawn(Pawn pawn, BoardCell target)
    {
        ArgumentNullException.ThrowIfNull(pawn);
        ArgumentNullException.ThrowIfNull(target);
        var source = GetCell(pawn.Row, pawn.Column);
        if (!ReferenceEquals(source.Occupant, pawn) || target.Occupant is not null)
        {
            throw new InvalidOperationException("The pawn cannot be moved to the target cell.");
        }

        source.Occupant = null;
        pawn.Row = target.Row;
        pawn.Column = target.Column;
        target.Occupant = pawn;
    }

    public void CapturePawn(Pawn pawn)
    {
        ArgumentNullException.ThrowIfNull(pawn);
        var cell = GetCell(pawn.Row, pawn.Column);
        if (!ReferenceEquals(cell.Occupant, pawn))
        {
            throw new InvalidOperationException("The pawn is not on the board.");
        }

        cell.Occupant = null;
        pawn.IsCaptured = true;
    }

    public IReadOnlyList<(int Row, int Column)> GetCornerCells() =>
        new[] { (0, 0), (0, Size - 1), (Size - 1, 0), (Size - 1, Size - 1) };

    public IReadOnlyList<(int Row, int Column)> GetStartingCells(int cornerIndex)
    {
        if (cornerIndex is < 0 or > 3)
        {
            throw new ArgumentOutOfRangeException(nameof(cornerIndex));
        }

        var top = cornerIndex < 2;
        var left = cornerIndex is 0 or 2;
        var row = top ? 0 : Size - 1;
        var column = left ? 0 : Size - 1;
        var rowStep = top ? 1 : -1;
        var columnStep = left ? 1 : -1;
        return new[] { (row, column), (row, column + columnStep), (row + rowStep, column) };
    }

    public BoardCell? GetNeighbor(int row, int column, Direction direction)
    {
        var (nextRow, nextColumn) = direction switch
        {
            Direction.Up => (row - 1, column),
            Direction.Down => (row + 1, column),
            Direction.Left => (row, column - 1),
            Direction.Right => (row, column + 1),
            _ => throw new ArgumentOutOfRangeException(nameof(direction))
        };

        return nextRow >= 0 && nextRow < Size && nextColumn >= 0 && nextColumn < Size
            ? Cells[nextRow, nextColumn]
            : null;
    }
}
