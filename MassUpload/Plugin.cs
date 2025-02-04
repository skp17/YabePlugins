/*
* @file            MassUpload.cs
* @brief
* @author          Steven Peters
* @date            Generated on 2024-02-20 at 12:00:00
* @note            
*
*/
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.BACnet;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Yabe;

//
// For simplicity all this code can be tested directly into Yabe project before exporting it
//

namespace MassUpload // namespace should have the same name as the dll file
{
    public class Plugin : IYabePlugin // class should be named Plugin and implementation of IYabePlugin is required 
    {
        YabeMainDialog yabeFrm;
        // yabeFrm is also declared into Yabe Main class
        // This is usefull for plugin developpement inside Yabe project, before exporting it
        public void Init(YabeMainDialog yabeFrm)
        {
            this.yabeFrm = yabeFrm;

            // Creates the menu Item
            ToolStripMenuItem MenuItem = new ToolStripMenuItem();
            MenuItem.Text = "Mass Upload";
            MenuItem.Click += new EventHandler(MenuItem_Click);

            // Add It as a sub menu (pluginsToolStripMenuItem is the only public Menu member)
            yabeFrm.pluginsToolStripMenuItem.DropDownItems.Add(MenuItem);
        }

        // Here only uses the content of the two Treeview into YabeMainDialog object
        // yabeFrm.m_AddressSpaceTree
        // yabeFrm.m_DeviceTree
        // DevicesObjectsName
        //
        // Also Trace.WriteLine can be used
        public void MenuItem_Click(object sender, EventArgs e)
        {
            try  // try catch all to avoid Yabe crash
            {
                Trace.WriteLine("Call to the MassUpload plugin");

                MassUpload frm = new MassUpload(yabeFrm);
                frm.ShowDialog();
            }
            catch (Exception ex)
            {
                Trace.TraceError(ex.Message);
            }
        }
    }
}
