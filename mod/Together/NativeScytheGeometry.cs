using Microsoft.Xna.Framework;
namespace Together;
// Stardew 1.6.15 MeleeWeapon area geometry; read-only, no RNG or game state writes.
internal static class NativeScytheGeometry {
	public static Rectangle Area(int x,int y,int facingDirection,Rectangle wielderBoundingBox,int indexInCurrentAnimation,int addedArea,int weaponType=0)
	{
		Rectangle result = Rectangle.Empty;
		int num;
		int num2;
		int num3;
		int num4;
		if (weaponType == 1)
		{
			num = 74;
			num2 = 48;
			num3 = 42;
			num4 = -32;
		}
		else
		{
			num = 64;
			num2 = 64;
			num4 = -32;
			num3 = 0;
		}
		if (weaponType == 1)
		{
			switch (facingDirection)
			{
			case 0:
				result = new Rectangle(x - num / 2, wielderBoundingBox.Y - num2 - num3, num / 2, num2 + num3);
				result.Offset(20, -16);
				result.Height += 16;
				result.Width += 20;
				break;
			case 1:
				result = new Rectangle(wielderBoundingBox.Right, y - num2 / 2 + num4, (int)((float)num2 * 1.15f), num);
				result.Offset(-4, 0);
				result.Width += 16;
				break;
			case 2:
				result = new Rectangle(x - num / 2, wielderBoundingBox.Bottom, num, (int)((float)num2 * 1.75f));
				result.Offset(12, -8);
				result.Width -= 21;
				break;
			case 3:
				result = new Rectangle(wielderBoundingBox.Left - (int)((float)num2 * 1.15f), y - num2 / 2 + num4, (int)((float)num2 * 1.15f), num);
				result.Offset(-12, 0);
				result.Width += 16;
				break;
			}
		}
		else
		{
			switch (facingDirection)
			{
			case 0:
				result = new Rectangle(x - num / 2, wielderBoundingBox.Y - num2 - num3, num, num2 + num3);
				switch (indexInCurrentAnimation)
				{
				case 5:
					result.Offset(76, -32);
					break;
				case 4:
					result.Offset(56, -32);
					result.Height += 32;
					break;
				case 3:
					result.Offset(40, -60);
					result.Height += 48;
					break;
				case 2:
					result.Offset(-12, -68);
					result.Height += 48;
					break;
				case 1:
					result.Offset(-48, -56);
					result.Height += 32;
					break;
				case 0:
					result.Offset(-60, -12);
					break;
				}
				break;
			case 2:
				result = new Rectangle(x - num / 2, wielderBoundingBox.Bottom, num, (int)((float)num2 * 1.5f));
				switch (indexInCurrentAnimation)
				{
				case 0:
					result.Offset(72, -92);
					break;
				case 1:
					result.Offset(56, -32);
					break;
				case 2:
					result.Offset(40, -28);
					break;
				case 3:
					result.Offset(-12, -8);
					break;
				case 4:
					result.Offset(-80, -24);
					result.Width += 32;
					break;
				case 5:
					result.Offset(-68, -44);
					break;
				}
				break;
			case 1:
				result = new Rectangle(wielderBoundingBox.Right, y - num2 / 2 + num4, num2, num);
				switch (indexInCurrentAnimation)
				{
				case 0:
					result.Offset(-44, -84);
					break;
				case 1:
					result.Offset(4, -44);
					break;
				case 2:
					result.Offset(12, -4);
					break;
				case 3:
					result.Offset(12, 37);
					break;
				case 4:
					result.Offset(-28, 60);
					break;
				case 5:
					result.Offset(-60, 72);
					break;
				}
				break;
			case 3:
				result = new Rectangle(wielderBoundingBox.Left - num2, y - num2 / 2 + num4, num2, num);
				switch (indexInCurrentAnimation)
				{
				case 0:
					result.Offset(56, -76);
					break;
				case 1:
					result.Offset(-8, -56);
					break;
				case 2:
					result.Offset(-16, -4);
					break;
				case 3:
					result.Offset(0, 37);
					break;
				case 4:
					result.Offset(24, 60);
					break;
				case 5:
					result.Offset(64, 64);
					break;
				}
				break;
			}
		}
		result.Inflate(addedArea, addedArea);
		return result;
	}
}
