// SoflanSupport.Setting — 新增类型, verbatim 自 head commit 2a7a4a4.
// MonoMod 将该类型整体复制进目标程序集 Assembly-CSharp.
using MAI2System;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace SoflanSupport
{
    public static class Setting
    {
        public static bool EnablePatchLog { set; get; } = true;

        private static bool init = false;

        static Setting()
        {
            if (init)
                return;
            init = true;

            using (var iniFile = new IniFile("mai2.ini"))
            {
                EnablePatchLog = iniFile.getValue("Patches", "EnablePatchLog", true);
            }

            PatchLog.WriteLine($"---------DpPatches.Setting------------");
            PatchLog.WriteLine($"EnablePatchLog = {EnablePatchLog}");
            PatchLog.WriteLine($"----------");
            PatchLog.WriteLine($"--------------------------------------");
        }
    }
}
