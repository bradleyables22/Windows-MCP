namespace Server.InteropServices
{
	/// <summary>
	/// Represents a point in virtual-screen coordinates.
	/// </summary>
	public readonly record struct ScreenPoint(int X, int Y);

	/// <summary>
	/// Represents a rectangle in virtual-screen coordinates.
	/// </summary>
	public readonly record struct ScreenRectangle(int X, int Y, int Width, int Height)
	{
		public int Left => X;
		public int Top => Y;
		public int Right => X + Width;
		public int Bottom => Y + Height;
		public int CenterX => X + (Width / 2);
		public int CenterY => Y + (Height / 2);
		public long Area => IsEmpty ? 0 : (long)Width * Height;
		public bool IsEmpty => Width <= 0 || Height <= 0;

		public static ScreenRectangle FromLTRB(int left, int top, int right, int bottom)
		{
			return new ScreenRectangle(left, top, right - left, bottom - top);
		}

		public bool Contains(ScreenPoint point)
		{
			return Contains(point.X, point.Y);
		}

		public bool Contains(int x, int y)
		{
			return x >= Left && x < Right && y >= Top && y < Bottom;
		}

		public ScreenRectangle Intersect(ScreenRectangle other)
		{
			var left = Math.Max(Left, other.Left);
			var top = Math.Max(Top, other.Top);
			var right = Math.Min(Right, other.Right);
			var bottom = Math.Min(Bottom, other.Bottom);

			if (right <= left || bottom <= top)
			{
				return new ScreenRectangle(left, top, 0, 0);
			}

			return FromLTRB(left, top, right, bottom);
		}
	}
}
