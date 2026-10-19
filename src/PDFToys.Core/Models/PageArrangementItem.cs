namespace PDFToys.Core.Models;

/// <summary>
/// A source page reference and clockwise rotation delta. Zero preserves the source rotation.
/// </summary>
/// <param name="SourcePageIndex">0-based index of the source page.</param>
/// <param name="RotationDegrees">Clockwise change in degrees, in multiples of 90, added to the effective source rotation.</param>
public sealed record PageArrangementItem(int SourcePageIndex, int RotationDegrees = 0);
