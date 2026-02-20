//
//  Faithful port of msdfgen by Viktor Chlumsky
//  https://github.com/Chlumsky/msdfgen
//

using System;

namespace NoZ.Editor.Msdf2;

/// <summary>
/// Edge color specifies which color channels an edge belongs to.
/// </summary>
[Flags]
internal enum EdgeColor
{
    BLACK = 0,
    RED = 1,
    GREEN = 2,
    YELLOW = 3,
    BLUE = 4,
    MAGENTA = 5,
    CYAN = 6,
    WHITE = 7
}
