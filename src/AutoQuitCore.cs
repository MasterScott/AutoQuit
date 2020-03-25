using ExileCore;
using ExileCore.PoEMemory;
using ExileCore.PoEMemory.Components;
using ExileCore.PoEMemory.MemoryObjects;
using ExileCore.Shared;
using ExileCore.Shared.Enums;
using ExileCore.Shared.Helpers;
using Input = ExileCore.Input;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace AutoQuit
{
    class AutoQuitCore : BaseSettingsPlugin<AutoQuitSettings>
    {
        private readonly int errmsg_time = 10;
        private ServerInventory flaskInventory = null;

        public AutoQuitCore()
        {
            Name = "AutoQuit";
        }

        public override bool Initialise()
        {
            //this.OnAreaChange += AutoQuitCore_OnAreaChange;
            //base.Initialise();

            return true;
        }

        public override void AreaChange(AreaInstance area)
        {
            flaskInventory = GameController.Game.IngameState.ServerData.GetPlayerInventoryByType(InventoryTypeE.Flask);
        }

        public void Quit()
        {
            CommandHandler.KillTCPConnectionForProcess(GameController.Window.Process.Id);
        }

        public override void Render()
        {
            // Panic Quit Key.
            if ((WinApi.GetAsyncKeyState(Settings.forcedAutoQuit) & 0x8000) != 0) Quit();

            if (GameController.Area.CurrentArea.IsTown || Settings.suspend && GameController.Area.CurrentArea.IsHideout) return;

            var localPlayer = GameController.Game.IngameState.Data.LocalPlayer;
            if (!Settings.Enable || !localPlayer.IsValid || !ShouldQuit(localPlayer)) return;

            try
            {
                Quit();
            }
            catch (Exception e)
            {
                LogError($"{e}", errmsg_time);
            }
        }
        private bool ShouldQuit(Entity localPlayer)
        {
            var playerHealth = localPlayer.GetComponent<Life>();
            bool gotCharges = this.gotCharges();

            var healthTooLow = Math.Round(playerHealth.HPPercentage, 3) * 100 < (Settings.percentHPQuit.Value);
            var energyShieldTooLow = playerHealth.MaxES > 0 && (Math.Round(playerHealth.ESPercentage, 3) * 100 < (Settings.percentESQuit.Value));

             /*Quit if HP flasks are empty", "0 -> Disable; 1 -> Enable; 2 -> Only when HP/ES min values was reached*/

            switch (Settings.emptyHPFlasksMode.Value)
            {
                case 0:
                    return healthTooLow || energyShieldTooLow;
                case 1:
                    return healthTooLow || energyShieldTooLow || !gotCharges;
                case 2:
                    return (healthTooLow || energyShieldTooLow) && !gotCharges;
                default:
                    return true;
            }
        }

        public bool gotCharges()
        {
            int charges = 0;
            var flaskList = getAllFlaskInfo();
            if (flaskList.Any())
            {
                foreach (Entity flask in flaskList)
                {
                    var CPU = flask.GetComponent<Charges>().ChargesPerUse;
                    var curCharges = flask.GetComponent<Charges>().NumCharges;
                    if (curCharges >= CPU)
                    {
                        charges += curCharges / CPU;
                    }
                }
                if (charges <= 0)
                {
                    return true;
                }
                return false;
            }
            return false;
        }

        public List<Entity> getAllFlaskInfo()
        {
            List<Entity> flaskList = new List<Entity>();

            if (flaskInventory == null)
            {
                flaskInventory = GameController.Game.IngameState.ServerData.GetPlayerInventoryByType(InventoryTypeE.Flask);
            }

            for (int i = 0; i < 5; i++)
            {
                var flask = flaskInventory[i, 0]?.Item;
                if (flask != null)
                {
                    var baseItem = GameController.Files.BaseItemTypes.Translate(flask.Path);
                    if (baseItem != null && baseItem.BaseName.Contains("Life Flask"))
                    {
                        flaskList.Add(flask);
                    }
                }
            }
            return flaskList;
        }
    }

    // Taken from ->
    // https://www.reddit.com/r/pathofexiledev/comments/787yq7/c_logout_app_same_method_as_lutbot/
    public static partial class CommandHandler
    {
        public static void KillTCPConnectionForProcess(int ProcessId)
        {
            MibTcprowOwnerPid[] table;
            var afInet = 2;
            var buffSize = 0;
            var ret = GetExtendedTcpTable(IntPtr.Zero, ref buffSize, true, afInet, TcpTableClass.TcpTableOwnerPidAll);
            var buffTable = Marshal.AllocHGlobal(buffSize);
            try
            {
                ret = GetExtendedTcpTable(buffTable, ref buffSize, true, afInet, TcpTableClass.TcpTableOwnerPidAll);
                if (ret != 0)
                    return;
                var tab = (MibTcptableOwnerPid)Marshal.PtrToStructure(buffTable, typeof(MibTcptableOwnerPid));
                var rowPtr = (IntPtr)((long)buffTable + Marshal.SizeOf(tab.dwNumEntries));
                table = new MibTcprowOwnerPid[tab.dwNumEntries];
                for (var i = 0; i < tab.dwNumEntries; i++)
                {
                    var tcpRow = (MibTcprowOwnerPid)Marshal.PtrToStructure(rowPtr, typeof(MibTcprowOwnerPid));
                    table[i] = tcpRow;
                    rowPtr = (IntPtr)((long)rowPtr + Marshal.SizeOf(tcpRow));

                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffTable);
            }

            //Kill Path Connection
            var PathConnection = table.FirstOrDefault(t => t.owningPid == ProcessId);
            PathConnection.state = 12;
            var ptr = Marshal.AllocCoTaskMem(Marshal.SizeOf(PathConnection));
            Marshal.StructureToPtr(PathConnection, ptr, false);
            SetTcpEntry(ptr);


        }

        [DllImport("iphlpapi.dll", SetLastError = true)]
        private static extern uint GetExtendedTcpTable(IntPtr pTcpTable, ref int dwOutBufLen, bool sort, int ipVersion, TcpTableClass tblClass, uint reserved = 0);

        [DllImport("iphlpapi.dll")]
        private static extern int SetTcpEntry(IntPtr pTcprow);

        [StructLayout(LayoutKind.Sequential)]
        public struct MibTcprowOwnerPid
        {
            public uint state;
            public uint localAddr;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)] public byte[] localPort;
            public uint remoteAddr;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)] public byte[] remotePort;
            public uint owningPid;

        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MibTcptableOwnerPid
        {
            public uint dwNumEntries;
            private readonly MibTcprowOwnerPid table;
        }

        private enum TcpTableClass
        {
            TcpTableBasicListener,
            TcpTableBasicConnections,
            TcpTableBasicAll,
            TcpTableOwnerPidListener,
            TcpTableOwnerPidConnections,
            TcpTableOwnerPidAll,
            TcpTableOwnerModuleListener,
            TcpTableOwnerModuleConnections,
            TcpTableOwnerModuleAll
        }
    }
}
