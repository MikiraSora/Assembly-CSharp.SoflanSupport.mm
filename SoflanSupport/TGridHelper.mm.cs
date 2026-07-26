// SoflanSupport.TGridHelper — 新增类型, verbatim 自 head commit 2a7a4a4.
using Manager;
using OngekiFumenEditor.Core.Base;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace SoflanSupport
{
    internal static class TGridHelper
    {
        public static TGrid ToTGrid(this NotesTime notesTime, NotesReader sr)
        {
            var resolution = sr.getResolution();
            var tGrid = new TGrid
            {
                Unit = notesTime.grid / resolution
            };
            tGrid.Grid = (int)(notesTime.grid - tGrid.Unit * resolution);
            return tGrid;
        }
    }
}
