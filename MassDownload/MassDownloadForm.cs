using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.BACnet;
using System.IO.BACnet.Serialize;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Yabe;

namespace MassDownload
{
    public partial class MassDownloadForm : Form
    {
        private YabeMainDialog _yabeFrm;

        private List<BacnetDeviceExport> _devices;

        private Dictionary<BacnetDeviceExport, byte> OriginalMaxInfoFrames;

        private Dictionary<Tuple<String, BacnetObjectId>, String> DevicesObjectsName { get { return _yabeFrm.DevicesObjectsName; } }
        private bool ObjectNamesChangedFlag { get { return _yabeFrm.objectNamesChangedFlag; } set { _yabeFrm.objectNamesChangedFlag = value; } }
        public IEnumerable<KeyValuePair<BacnetClient, YabeMainDialog.BacnetDeviceLine>> YabeDiscoveredDevices { get { return _yabeFrm.DiscoveredDevices; } }

        public MassDownloadForm(YabeMainDialog yabeFrm)
        {
            this._yabeFrm = yabeFrm;
            Icon = yabeFrm.Icon; // gets Yabe Icon
            OriginalMaxInfoFrames = new Dictionary<BacnetDeviceExport, byte>();

            InitializeComponent();
            treeView1.AfterCheck += node_AfterCheck;
            numericUpDownMax.Value = 20;
        }

        private void MassDownload_Shown(object sender, EventArgs e)
        {
            Cursor.Current = Cursors.WaitCursor;
            try
            {
                DiscoverDeviceFiles();
            }
            catch (Exception) { }
            Cursor.Current = Cursors.Default;
        }

        private void MassDownloadForm_FormClosed(object sender, FormClosedEventArgs e)
        {
            foreach (KeyValuePair<BacnetDeviceExport, byte> entry in OriginalMaxInfoFrames)
            {
                BacnetDeviceExport device = (BacnetDeviceExport)entry.Key;
                byte originalMaxInfoFrames = (byte)entry.Value;
                if (device.Comm.Transport is BacnetMstpProtocolTransport transport) 
                    transport.MaxInfoFrames = originalMaxInfoFrames;
            }
        }

        private void DiscoverDeviceFiles()
        {
            treeView1.Nodes.Clear();
            treeView1.Update();
            
            _devices = PopulateDevicesWithNames();
            _devices.Sort();

            downloadButton.Enabled = _devices.Count > 0; // Enable the download button if devices are discovered

            foreach (BacnetDeviceExport device in _devices)
            {
                // Add device information to treeview
                TreeNode deviceTreeNode = new TreeNode();
                deviceTreeNode.Text = device.Name;
                deviceTreeNode.Tag = device;
                treeView1.Nodes.Add(deviceTreeNode);
                treeView1.Update();

                if (device.Comm.Transport is BacnetMstpProtocolTransport transport)
                    OriginalMaxInfoFrames.Add(device, transport.MaxInfoFrames);

                int minInstance = (int)numericUpDownMin.Value;
                int maxInstance = (int)numericUpDownMax.Value;

                for (int i = minInstance; i <= maxInstance; i++)
                {
                    try
                    {
                        // Find all the object files of each device within a certain range of instances
                        if (device.Comm.ReadPropertyRequest(device.DeviceAddress, new BacnetObjectId(BacnetObjectTypes.OBJECT_FILE, (uint)i), BacnetPropertyIds.PROP_OBJECT_IDENTIFIER, out IList<BacnetValue> value_list))
                        {
                            BacnetObjectId bobj_id = new BacnetObjectId(BacnetObjectTypes.OBJECT_FILE, (uint)i);
                            AddPointToDeviceNode(deviceTreeNode, bobj_id);
                        }
                    }
                    catch (Exception ex)
                    {
                        Trace.TraceError(ex.Message);
                    }

                    deviceTreeNode.Expand();
                }
                
            }
            Application.DoEvents();
        }

        public void GetDeviceObjectsName(BacnetDeviceExport device, BacnetObjectId bobj_id, out string objectName)
        {
            bool Prop_Object_NameOK = false;
            objectName = "";

            lock (DevicesObjectsName)
            {
                Prop_Object_NameOK = DevicesObjectsName.TryGetValue(new Tuple<string, BacnetObjectId>(device.DeviceAddress.FullHashString(), bobj_id), out objectName);
            }
            if (!Prop_Object_NameOK)
            {
                try
                {
                    IList<BacnetValue> values;
                    if (device.Comm.ReadPropertyRequest(device.DeviceAddress, bobj_id, BacnetPropertyIds.PROP_OBJECT_NAME, out values))
                    {
                        objectName = values[0].ToString();
                    }
                }
                catch (Exception) { }
            }
        }

        public List<BacnetDeviceExport> PopulateDevicesWithNames(bool commandProgBar = false)
        {
            int progTotal = YabeDiscoveredDevices.Count() + 1;
            //int prog = 0;
            List<BacnetDeviceExport> deviceList = new List<BacnetDeviceExport>();
            foreach (KeyValuePair<BacnetClient, YabeMainDialog.BacnetDeviceLine> transport in YabeDiscoveredDevices)
            {
                foreach (KeyValuePair<BacnetAddress, uint> address in transport.Value.Devices)
                {
                    BacnetAddress deviceAddress = address.Key;
                    uint deviceID = address.Value;
                    BacnetClient comm = transport.Key;
                    BacnetDeviceExport device = new BacnetDeviceExport(comm, this, deviceID, deviceAddress);

                    bool Prop_Object_NameOK = false;
                    BacnetObjectId deviceObjectID = new BacnetObjectId(BacnetObjectTypes.OBJECT_DEVICE, deviceID);
                    string identifier = null;

                    lock (DevicesObjectsName)
                    {
                        Prop_Object_NameOK = DevicesObjectsName.TryGetValue(new Tuple<String, BacnetObjectId>(deviceAddress.FullHashString(), deviceObjectID), out identifier);
                    }

                    if (Prop_Object_NameOK)
                    {
                        identifier = identifier + " [" + deviceObjectID.Instance.ToString() + "] ";
                    }
                    else
                    {
                        try
                        {
                            IList<BacnetValue> values;
                            if (comm.ReadPropertyRequest(deviceAddress, deviceObjectID, BacnetPropertyIds.PROP_OBJECT_NAME, out values))
                            {
                                identifier = values[0].ToString();
                                lock (DevicesObjectsName)
                                {
                                    Tuple<String, BacnetObjectId> t = new Tuple<String, BacnetObjectId>(deviceAddress.FullHashString(), deviceObjectID);
                                    DevicesObjectsName.Remove(t);
                                    DevicesObjectsName.Add(t, identifier);
                                    ObjectNamesChangedFlag = true;
                                }
                                identifier = identifier + " [" + deviceObjectID.Instance.ToString() + "] ";
                            }
                        }
                        catch { }
                    }

                    if (identifier != null)
                    {
                        device.Name = identifier;
                    }

                    if (deviceList.Find(item => item.DeviceID == deviceID) == null)
                    {
                        deviceList.Add(device);
                    }

                    // Subscribe WhoHas request

                }
                //if (commandProgBar)
                //{
                //    prog++;
                //    progBar.Value = (int)(100 * prog / progTotal);
                //    Application.DoEvents();
                //}
            }
            return deviceList;
        }

        public void AddPointToDeviceNode(TreeNode deviceTreeNode, BacnetObjectId bobj_id)
        {
            BacnetDeviceExport device = (BacnetDeviceExport)deviceTreeNode.Tag;
            BacnetPointExport point = new BacnetPointExport(device, bobj_id);
            TreeNode pointTreeNode = new TreeNode();

            GetDeviceObjectsName(device, bobj_id, out string objectName);
            point.Name = $"{objectName} [{bobj_id}]";
            point.ObjectName = objectName;
            pointTreeNode.Text = point.Name;
            pointTreeNode.Tag = point;
            deviceTreeNode.Nodes.Add(pointTreeNode);
            treeView1.Update();
        }

        public void DisplayDeviceFiles()
        {
            treeView1.Nodes.Clear();
            treeView1.Update();

            // Add a root TreeNode for each Device
            foreach (BacnetDeviceExport device in _devices)
            {
                TreeNode deviceTreeNode = new TreeNode();
                deviceTreeNode.Text = device.Name;
                deviceTreeNode.Tag = device;
                treeView1.Nodes.Add(deviceTreeNode);
                foreach (BacnetPointExport point in device.Points)
                {
                    TreeNode pointTreeNode = new TreeNode();
                    pointTreeNode.Text = point.Name;
                    pointTreeNode.Tag = point;
                    treeView1.Nodes[_devices.IndexOf(device)].Nodes.Add(pointTreeNode);
                }
            }

            treeView1.ExpandAll();
            treeView1.EndUpdate();
        }

        private List<BacnetObjectId> SortBacnetObjects(IList<BacnetValue> value_list)
        {
            return _yabeFrm.SortBacnetObjects(value_list);
        }

        // Updates all child tree nodes recursively.
        private void CheckAllChildNodes(TreeNode treeNode, bool nodeChecked)
        {
            foreach (TreeNode node in treeNode.Nodes)
            {
                node.Checked = nodeChecked;
                if (node.Nodes.Count > 0)
                {
                    // If the current node has child nodes, call the CheckAllChildsNodes method recursively.
                    this.CheckAllChildNodes(node, nodeChecked);
                }
            }
        }

        // After a tree node's Checked property is changed, all its child nodes are updated to the same value.
        private void node_AfterCheck(object sender, TreeViewEventArgs e)
        {
            // The code only executes if the user caused the checked state to change.
            if (e.Action != TreeViewAction.Unknown)
            {
                if (e.Node.Nodes.Count > 0)
                {
                    /* Calls the CheckAllChildNodes method, passing in the current 
                    Checked value of the TreeNode whose checked state changed. */
                    this.CheckAllChildNodes(e.Node, e.Node.Checked);
                }
            }
        }

        private byte ReturnNumberOfCheckedChildNodes(TreeNode treenode)
        {
            byte count = 0;
            foreach (TreeNode node in treenode.Nodes)
            {
                if (node.Checked)
                    count++;
            }

            return count;
        }

        private void SetMaxInfoFrames(BacnetMstpProtocolTransport transport, byte max_info_frames)
        {
            transport.MaxInfoFrames = max_info_frames;
        }

        public class BacnetDeviceExport : IEquatable<BacnetDeviceExport>, IComparable<BacnetDeviceExport>
        {
            public uint DeviceID { get; }
            private string _name;
            public string Name { get { return _name; } set { _nameIsSet = true; _name = value; } }
            private bool _nameIsSet;
            public bool NameIsSet { get { return _nameIsSet; } }
            public BacnetAddress DeviceAddress { get; }
            public BacnetClient Comm { get; }
            public MassDownloadForm ParentWindow { get; }
            public List<BacnetPointExport> Points { get; }  // todo: change to represent File objects

            public override string ToString()
            {
                return Name;
            }

            public bool Equals(BacnetDeviceExport other)
            {
                return Name.Equals(other.Name, StringComparison.OrdinalIgnoreCase);
            }

            public int CompareTo(BacnetDeviceExport other)
            {
                return Name.CompareTo(other.Name);
            }

            public BacnetDeviceExport(BacnetClient comm, MassDownloadForm parentWindow, uint deviceID, BacnetAddress deviceAddress)
            {
                Comm = comm;
                ParentWindow = parentWindow;
                DeviceID = deviceID;
                BacnetObjectId deviceObjectID = new BacnetObjectId(BacnetObjectTypes.OBJECT_DEVICE, deviceID);
                _name = deviceObjectID.ToString();
                _nameIsSet = false;
                DeviceAddress = deviceAddress;
                Points = new List<BacnetPointExport>();
            }
        }

        public class BacnetPointExport : IEquatable<BacnetPointExport>, IComparable<BacnetPointExport>
        {
            public BacnetDeviceExport ParentDevice { get; }
            public BacnetObjectId ObjectID { get; }
            private string _name;
            public string Name { get { return _name; } set { _nameIsSet = true; _name = value; } }
            private bool _nameIsSet;
            public bool NameIsSet { get { return _nameIsSet; } }
            private string _objectName;
            public string ObjectName { get { return _objectName; } set { _objectNameIsSet = true; _objectName = value; } }
            private bool _objectNameIsSet;
            public bool ObjectNameIsSet { get { return _objectNameIsSet; } }

            public override string ToString()
            {
                return Name;
            }

            public bool Equals(BacnetPointExport other)
            {
                return Name.Equals(other.Name, StringComparison.OrdinalIgnoreCase);
            }

            public int CompareTo(BacnetPointExport other)
            {
                return Name.CompareTo(other.Name);
            }
            public BacnetPointExport(BacnetDeviceExport parentDevice, BacnetObjectId objectID)
            {
                ObjectID = objectID;
                ParentDevice = parentDevice;
                _name = objectID.ToString();
                _nameIsSet = false;
            }
        }

        public class FileTransfers
        {
            public bool Cancel { get; set; }

            // Define custom events to report progress and errors
            public event EventHandler<int> ProgressEvent;
            public event EventHandler<string> ErrorEvent;

            // Method to invoke the progress event
            public void OnProgress(int position)
            {
                ProgressEvent?.Invoke(null, position);
            }

            // Method to invoke the error event
            public void OnError(string error)
            {
                ErrorEvent?.Invoke(null, error);
            }

            public static int ReadFileSize(BacnetClient comm, BacnetAddress adr, BacnetObjectId object_id)
            {
                IList<BacnetValue> value;
                try
                {
                    if (!comm.ReadPropertyRequest(adr, object_id, BacnetPropertyIds.PROP_FILE_SIZE, out value))
                        return -1;
                    if (value == null || value.Count == 0)
                        return -1;
                    return (int)Convert.ChangeType(value[0].Value, typeof(int));
                }
                catch
                {
                    return -1;
                }
            }

            public void DownloadFileByBlocking(BacnetClient comm, BacnetAddress adr, BacnetObjectId object_id, string filename, Action<int> progress_action)
            {
                Cancel = false;

                //open file
                System.IO.FileStream fs = null;
                try
                {
                    fs = System.IO.File.Create(filename);
                }
                catch (Exception ex)
                {
                    throw new System.IO.IOException("Couldn't open file", ex);
                }

                int position = 0;
                uint count = (uint)comm.GetFileBufferMaxSize();
                bool end_of_file = false;
                byte[] buffer;
                int buffer_offset;
                try
                {
                    while (!end_of_file && !Cancel)
                    {
                        //read from device
                        if (!comm.ReadFileRequest(adr, object_id, ref position, ref count, out end_of_file, out buffer, out buffer_offset))
                            throw new System.IO.IOException("Couldn't read file");
                        position += (int)count;

                        //write to file
                        if (count > 0)
                        {
                            fs.Write(buffer, buffer_offset, (int)count);
                            if (progress_action != null) progress_action(position);
                        }
                    }
                }
                finally
                {
                    fs.Close();
                }
            }

            public void DownloadFileBySegmentation(BacnetClient comm, BacnetAddress adr, BacnetObjectId object_id, string filename, Action<int> progress_action)
            {
                Cancel = false;

                //open file
                System.IO.FileStream fs = null;
                try
                {
                    fs = System.IO.File.Create(filename);
                }
                catch (Exception ex)
                {
                    throw new System.IO.IOException("Couldn't open file", ex);
                }

                BacnetMaxSegments old_segments = comm.MaxSegments;
                comm.MaxSegments = BacnetMaxSegments.MAX_SEG65;     //send as many segments as needed
                //comm.ProposedWindowSize = Properties.Settings.Default.Segments_ProposedWindowSize;      //set by options
                comm.ForceWindowSize = true;
                try
                {
                    int position = 0;
                    //uint count = (uint)comm.GetFileBufferMaxSize() * 20;     //this is more realistic
                    uint count = 50000;                                        //this is more difficult
                    bool end_of_file = false;
                    byte[] buffer;
                    int buffer_offset;
                    while (!end_of_file && !Cancel)
                    {
                        //read from device
                        if (!comm.ReadFileRequest(adr, object_id, ref position, ref count, out end_of_file, out buffer, out buffer_offset))
                            throw new System.IO.IOException("Couldn't read file");
                        position += (int)count;

                        //write to file
                        if (count > 0)
                        {
                            fs.Write(buffer, buffer_offset, (int)count);
                            if (progress_action != null) progress_action(position);
                        }
                    }
                }
                finally
                {
                    fs.Close();
                    comm.MaxSegments = old_segments;
                    comm.ForceWindowSize = false;
                }
            }

            /// <summary>
            /// This method is based upon increasing the MaxInfoFrames in the MSTP.
            /// In Bacnet/IP this will have bad effect due to the retries
            /// </summary>
            public void DownloadFileByAsync(BacnetClient comm, BacnetAddress adr, BacnetObjectId object_id, string filename, Action<int> progress_action)
            {
                Cancel = false;

                //open file
                System.IO.FileStream fs = null;
                try
                {
                    fs = System.IO.File.Create(filename);
                }
                catch (Exception ex)
                {
                    throw new System.IO.IOException("Couldn't open file", ex);
                }

                uint max_count = (uint)comm.GetFileBufferMaxSize();
                BacnetAsyncResult[] transfers = new BacnetAsyncResult[50];
                byte old_max_info_frames = comm.Transport.MaxInfoFrames;
                comm.Transport.MaxInfoFrames = 50;      //increase max_info_frames so that we can occupy line more. This might be against 'standard'

                try
                {
                    int position = 0;
                    bool eof = false;

                    while (!eof && !Cancel)
                    {
                        //start many async transfers
                        for (int i = 0; i < transfers.Length; i++)
                            transfers[i] = (BacnetAsyncResult)comm.BeginReadFileRequest(adr, object_id, position + i * (int)max_count, max_count, false);

                        //wait for all transfers to finish
                        int current = 0;
                        int retries = comm.Retries;
                        while (current < transfers.Length)
                        {
                            if (!transfers[current].WaitForDone(comm.Timeout))
                            {
                                if (--retries > 0)
                                {
                                    transfers[current].Resend();
                                    continue;
                                }
                                else
                                    throw new System.IO.IOException("Couldn't read file");
                            }
                            retries = comm.Retries;

                            uint count;
                            byte[] file_buffer;
                            int file_buffer_offset;
                            Exception ex;
                            comm.EndReadFileRequest(transfers[current], out count, out position, out eof, out file_buffer, out file_buffer_offset, out ex);
                            transfers[current] = null;
                            if (ex != null) throw ex;

                            if (count > 0)
                            {
                                //write to file
                                fs.Position = position;
                                fs.Write(file_buffer, file_buffer_offset, (int)count);
                                position += (int)count;
                                if (progress_action != null) progress_action(position);
                            }
                            current++;
                        }
                    }
                }
                finally
                {
                    fs.Close();
                    comm.Transport.MaxInfoFrames = old_max_info_frames;
                }
            }

            public async Task DownloadFileAsync(BacnetClient comm, BacnetAddress adr, BacnetObjectId object_id, string filename, Action<int> progress_action, CancellationToken cancellationToken)
            {
                //Cancel = false;

                using (var fs = new System.IO.FileStream(filename, System.IO.FileMode.Create))
                {
                    int position = 0;
                    uint count = (uint)comm.GetFileBufferMaxSize();
                    bool end_of_file = false;
                    byte[] buffer;
                    int buffer_offset;

                    // Event to report progress
                    EventHandler<int> progressEventHandler = null;

                    if (progress_action != null)
                    {
                        // Subscribe to the progress event
                        progressEventHandler = (s, progress) =>
                        {
                            progress_action(progress);
                        };

                        // Subscribe to the event
                        this.ProgressEvent += progressEventHandler;
                    }

                    try
                    {
                        await Task.Run(() =>
                        {
                            try
                            {
                                while (!end_of_file)
                                {
                                    if (cancellationToken.IsCancellationRequested)
                                    {
                                        cancellationToken.ThrowIfCancellationRequested();
                                    }

                                    // Read from device
                                    if (!comm.ReadFileRequest(adr, object_id, ref position, ref count, out end_of_file, out buffer, out buffer_offset))
                                        throw new System.IO.IOException("Couldn't read file");

                                    position += (int)count;

                                    // Write to file
                                    if (count > 0)
                                    {
                                        fs.Write(buffer, buffer_offset, (int)count);
                                        //progress_action?.Invoke(position);

                                        // Report progress using an event
                                        this.OnProgress(position);
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                // Handle exceptions appropriately
                                // You may want to log the exception or take other actions
                                Trace.TraceError($"Error: {ex.Message}");
                                this.OnError(ex.Message);
                            }
                            finally
                            {
                                // Unsubscribe from the event when done
                                if (progressEventHandler != null)
                                {
                                    this.ProgressEvent -= progressEventHandler;
                                }
                            }

                        });
                    }
                    finally
                    {
                        // Ensure proper disposal of the FileStream
                        fs.Close();
                    }
                }
            }

            public void UploadFileByBlocking(BacnetClient comm, BacnetAddress adr, BacnetObjectId object_id, string filename, Action<int> progress_action)
            {
                Cancel = false;

                //open file
                System.IO.FileStream fs = null;
                try
                {
                    fs = System.IO.File.OpenRead(filename);
                }
                catch (Exception ex)
                {
                    throw new System.IO.IOException("Couldn't open file", ex);
                }

                try
                {
                    int position = 0;
                    int count = comm.GetFileBufferMaxSize();
                    byte[] buffer = new byte[count];
                    while (count > 0 && !Cancel)
                    {
                        //read from disk
                        count = fs.Read(buffer, 0, count);
                        if (count < 0)
                            throw new System.IO.IOException("Couldn't read file");
                        else if (count == 0)
                            continue;

                        //write to device
                        if (!comm.WriteFileRequest(adr, object_id, ref position, count, buffer))
                            throw new System.IO.IOException("Couldn't write file");

                        //progress
                        if (count > 0)
                        {
                            position += count;
                            if (progress_action != null) progress_action(position);
                        }
                    }
                }
                finally
                {
                    fs.Close();
                }
            }
        }

        public class ListViewItemBetterString : ListViewItem
        {
            public override string ToString()
            {
                if (!string.IsNullOrEmpty(Name))
                {
                    return Name;
                }
                else if (!string.IsNullOrEmpty(Text))
                {
                    return Text;
                }
                else if (Tag != null)
                {
                    return Tag.ToString();
                }
                else
                {
                    return base.ToString();
                }
            }
        }

        private void UpdateTreeNodeProgress(TreeNode treeNode, int progress)
        {
            if (treeNode.TreeView.InvokeRequired)
            {
                treeNode.TreeView.Invoke((MethodInvoker)(() => UpdateTreeNodeProgress(treeNode, progress)));
            }
            else
            {
                // Update the text of the TreeNode to reflect the progress
                string pattern = @"(\d){1,3}% complete";
                string text = treeNode.Text;
                Regex regex = new Regex(pattern);
                Match match = regex.Match(text);
                if (match.Success)
                    treeNode.Text = Regex.Replace(treeNode.Text, pattern, $"{progress}% complete");
                else
                    treeNode.Text = $"{treeNode.Text} | {progress}% complete";
            }
        }

        private void DisplayErrorMessage(TreeNode treeNode, string errorMessage)
        {
            if (treeNode.TreeView.InvokeRequired)
            {
                treeNode.TreeView.Invoke((MethodInvoker)(() => DisplayErrorMessage(treeNode, errorMessage)));
            }
            else
            {
                // Update the text of the TreeNode to reflect the progress
                treeNode.Text = $"{treeNode.Text} | Error: {errorMessage}";
            }
        }

        public async Task<bool> DownloadFile(BacnetPointExport pointExport, string directory, TreeNode treeNode, CancellationToken cancellationToken)
        {
            bool success = false;

            try
            {
                BacnetClient comm = pointExport.ParentDevice.Comm;
                BacnetAddress adr = pointExport.ParentDevice.DeviceAddress;
                BacnetObjectId object_id = pointExport.ObjectID;
                string filename = pointExport.ParentDevice.Name + " - " + pointExport.ObjectName;
                int filesize = FileTransfers.ReadFileSize(comm, adr, object_id);

                // Subscribe to the progress event to receive updates
                EventHandler<int> progressEventHandler = (s, position) =>
                {
                    if (filesize > 0)
                    {
                        int progress = (100 * position / filesize);
                        UpdateTreeNodeProgress(treeNode, progress);
                    }
                };

                // Subscribe to the error event to receive error messages
                EventHandler<string> errorEventHandler = (s, errorMessage) =>
                {
                    DisplayErrorMessage(treeNode, errorMessage);
                };

                FileTransfers transfer = new FileTransfers();
                transfer.ProgressEvent += progressEventHandler;
                transfer.ErrorEvent += errorEventHandler;

                // Was cancellation already requested before the task could start?
                if (cancellationToken.IsCancellationRequested)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }

                Application.DoEvents();
                try
                {
                    //transfer.DownloadFileByAsync(comm, adr, object_id, Path.Combine(directory, filename), null);
                    await transfer.DownloadFileAsync(comm, adr, object_id, Path.Combine(directory, filename), null, cancellationToken);
                    success = true;
                }
                finally
                {
                    // Unsubscribe from the progress event
                    transfer.ProgressEvent -= progressEventHandler;
                    transfer.ErrorEvent -= errorEventHandler;
                }
            }
            catch (Exception ex)
            {
                Trace.TraceError(ex.Message);
            }

            return success;
        }

        private async void downloadButton_Click(object sender, EventArgs e)
        {
            string directory = "";
            var tasks = new List<Task>();
            var tokenSource = new CancellationTokenSource();
            var token = tokenSource.Token;

            // Get download path
            FolderBrowserDialog folderBrowserDialog = new FolderBrowserDialog();
            if (folderBrowserDialog.ShowDialog() == DialogResult.OK)
            {
                directory = folderBrowserDialog.SelectedPath;
            }

            // Event handler to cancel the operation
            EventHandler cancelHandler = (s, a) =>
            {
                tokenSource.Cancel();
            };

            // Subscribe to the event
            cancelButton.Click += cancelHandler;

            if (!string.IsNullOrEmpty(directory))
            {
                try
                {
                    // Iterate through list of selected nodes
                    foreach (TreeNode deviceTreeNode in treeView1.Nodes)
                    {
                        // Check to see if comm type in MSTP
                        BacnetDeviceExport device = (BacnetDeviceExport)deviceTreeNode.Tag;
                        if (device.Comm.Transport is BacnetMstpProtocolTransport transport && transport.Type == BacnetAddressTypes.MSTP)
                        {
                            byte numberOfCheckedNodes = ReturnNumberOfCheckedChildNodes(deviceTreeNode);
                            if (numberOfCheckedNodes > 0) 
                                SetMaxInfoFrames((BacnetMstpProtocolTransport)device.Comm.Transport, numberOfCheckedNodes);
                        }

                        foreach (TreeNode pointTreeNode in deviceTreeNode.Nodes)
                        {
                            BacnetPointExport point = (BacnetPointExport)pointTreeNode.Tag;

                            if (pointTreeNode.Checked == true)
                            {
                                tasks.Add(DownloadFile(point, directory, pointTreeNode, token));

                                // Reset Node text in case the progress and/or errors are displayed from the previous download
                                pointTreeNode.Text = point.Name;
                            }
                        }
                    }

                    // Await all tasks
                    await Task.WhenAll(tasks);

                    if (!token.IsCancellationRequested)
                    {
                        MessageBox.Show(this, "Download complete", "Done", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                }
                catch (Exception)
                {
                    MessageBox.Show(this, "An error occurred during download.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                finally
                {
                    cancelButton.Click -= cancelHandler;
                    tokenSource.Dispose();
                }
            }
        }

        private void refreshButton_Click(object sender, EventArgs e)
        {
            Cursor.Current = Cursors.WaitCursor;
            treeView1.Nodes.Clear();
            treeView1.Update();
            try
            {
                DiscoverDeviceFiles();
            }
            catch (Exception) { }
            Cursor.Current = Cursors.Default;
        }

        private void selectAllButton_Click(object sender, EventArgs e)
        {
            foreach (TreeNode deviceTreeNode in treeView1.Nodes)
            {
                deviceTreeNode.Checked = true;
                CheckAllChildNodes(deviceTreeNode, true);
            }
        }

        private void unSelectAllButton_Click(object sender, EventArgs e)
        {
            foreach (TreeNode deviceTreeNode in treeView1.Nodes)
            {
                deviceTreeNode.Checked = false;
                CheckAllChildNodes(deviceTreeNode, false);
            }
        }

        private void numericUpDownMax_ValueChanged(object sender, EventArgs e)
        {
            numericUpDownMin.Maximum = numericUpDownMax.Value;
        }
    }
}
