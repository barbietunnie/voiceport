/* 
 * Copyright (C) 2008 Sasa Coh <sasacoh@gmail.com>
 *
 * This program is free software; you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation; either version 2 of the License, or
 * (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty ofF
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License
 * along with this program; if not, write to the Free Software
 * Foundation, Inc., 59 Temple Place, Suite 330, Boston, MA  02111-1307  USA 
 * 
 * WaveLib library sources http://www.codeproject.com/KB/graphics/AudioLib.aspx
 * 
 * Visit SipekSDK page at http://voipengine.googlepages.com/
 * 
 * Visit SIPek's home page at http://sipekphone.googlepages.com/ 
 * 
 */

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using System.Collections.ObjectModel;
using System.Windows.Forms.Design;
using System.IO.Ports;
using Sipek.Common;
using Sipek.Common.CallControl;

#if LINUX
#else
using WaveLib.AudioMixer; // see http://www.codeproject.com/KB/graphics/AudioLib.aspx
#endif

namespace Sipek
{
  public partial class MainForm : Form
  {
    const string HEADER_TEXT = "voicePort";
 
    Timer tmr = new Timer();  // Refresh Call List
    EUserStatus _lastUserStatus = EUserStatus.AVAILABLE;
    public SerialPort comPort;
    public bool isOK;

    public bool IsInitialized
    {
      get { return SipekResources.StackProxy.IsInitialized; }
    }


    #region Properties
    private SipekResources _resources = null;
    private SipekResources SipekResources
    {
      get { return _resources; }
    }
    
    #endregion

    public MainForm()
    {
      InitializeComponent();

      _resources = new SipekResources(this);
    }

    /////////////////////////////////////////////////////////////////////////////////

    private void RefreshForm()
    {
      if (IsInitialized) 
      {
        // Update Call Status
        UpdateCallLines(-1);

        // Update Call Register
        UpdateCallRegister();

        RegState();

      }

      // Refresh toolstripbuttons
      toolStripButtonDND.Checked = SipekResources.Configurator.DNDFlag;
      toolStripButtonAA.Checked = SipekResources.Configurator.AAFlag;

      unconditionalToolStripMenuItem.Checked = SipekResources.Configurator.CFUFlag;
      toolStripTextBoxCFUNumber.Text = SipekResources.Configurator.CFUNumber;

      noReplyToolStripMenuItem.Checked = SipekResources.Configurator.CFNRFlag;
      toolStripTextBoxCFNRNumber.Text = SipekResources.Configurator.CFNRNumber;

      busyToolStripMenuItem.Checked = SipekResources.Configurator.CFBFlag;
      toolStripTextBoxCFBNumber.Text = SipekResources.Configurator.CFBNumber;

      // check if user status available
      toolStripComboBoxUserStatus.Enabled = SipekResources.Configurator.PublishEnabled;
    }



    /// <summary>
    /// 
    /// </summary>
    private void UpdateCallRegister()
    {
      lock (this)
      {
        listViewCallRegister.Items.Clear();
        // Update Dial field
        toolStripComboDial.Items.Clear();

        Stack<CCallRecord> results = SipekResources.CallLogger.getList();

        int cnt = 0; int dialedcnt = 0;
        foreach (CCallRecord item in results)
        {
          string duration = item.Duration.ToString();
          if (duration.IndexOf('.') > 0) duration = duration.Remove(duration.IndexOf('.')); // remove miliseconds

          string recorditem = item.Number;


          ListViewItem lvi = new ListViewItem(new string[] {
               item.Type.ToString(), recorditem.Trim(), item.Time.ToString(), duration});

          lvi.Tag = item;

          listViewCallRegister.Items.Insert(cnt, lvi);

          // add item to dial combo (if dialed)
          if (item.Type == ECallType.EDialed)
          {
            toolStripComboDial.Items.Insert(dialedcnt++, item.Number);
          }
          // increase counter
          cnt++;
        }
      }
    }

    //////////////////////////////////////////////////////////////////////////////////////
    /// Register callbacks and synchronize threads
    /// 
    delegate void DRefreshForm();
    delegate void DCallStateChanged(int sessionId);
    delegate void MessageReceivedDelegate(string from, string message);
    delegate void BuddyStateChangedDelegate(int buddyId, int status, string text);
    delegate void DMessageWaiting(int mwi, string text);

    public void onCallStateChanged(int sessionId)
    {
      if (InvokeRequired)
        this.BeginInvoke(new DRefreshForm(this.RefreshForm));
      else
        RefreshForm();
    }


    public void onAccountStateChanged(int accId, int accState)
    {
      if (InvokeRequired)
        this.BeginInvoke(new DRefreshForm(this.RefreshForm));
      else
        RefreshForm();
    }




    private void exitToolStripMenuItem_Click(object sender, EventArgs e)
    {
      this.Close();
    }



    private void toolStripMenuItem1_Click(object sender, EventArgs e)
    {

    }

    /// <summary>
    /// Enable or disable menu items regarding to call state...
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void contextMenuStripCalls_Opening(object sender, CancelEventArgs e)
    {
      // Hide all items...
      foreach (ToolStripMenuItem mi in contextMenuStripCalls.Items)
      {
        mi.Visible = false;
      }

      if (listViewCallLines.SelectedItems.Count > 0)
      {
        ListViewItem lvi = listViewCallLines.SelectedItems[0];

        if (SipekResources.CallManager.Count <= 0)
        {
          return;
        }
        else
        {
          EStateId stateId = ((CStateMachine)lvi.Tag).StateId;
          switch (stateId)
          {
            case EStateId.INCOMING:
              acceptToolStripMenuItem.Visible = true;
              transferToolStripMenuItem.Visible = true;
              break;
            case EStateId.ACTIVE:
              holdRetrieveToolStripMenuItem.Text = "Hold";
              holdRetrieveToolStripMenuItem.Visible = true;
              transferToolStripMenuItem.Visible = true;
              //sendMessageToolStripMenuItem.Visible = true;
              break;
            case EStateId.HOLDING:
              holdRetrieveToolStripMenuItem.Text = "Retrieve";
              holdRetrieveToolStripMenuItem.Visible = true;
              break;
          }

        }
        // call
        releaseToolStripMenuItem.Visible = true;
      }
    }

    ///////////////////////////////////////////////////////////////////////////////////
    // Call Related Methods
    #region Call Related Methods

    /// <summary>
    /// UpdateCallLines delegate
    /// </summary>
    private void UpdateCallLines(int sessionId)
    {     
      listViewCallLines.Items.Clear();

      try
      {
        // get entire call list
        Dictionary<int, IStateMachine> callList = SipekResources.CallManager.CallList;

        foreach (KeyValuePair<int, IStateMachine> kvp in callList)
        {
          string number = kvp.Value.CallingNumber;
          string name = kvp.Value.CallingName; 

          string duration = kvp.Value.Duration.ToString();
          if (duration.IndexOf('.') > 0) duration = duration.Remove(duration.IndexOf('.')); // remove miliseconds
          // show name & number or just number
          string display = name.Length > 0 ? name + " / " + number : number;
          string stateName = kvp.Value.StateId.ToString();
          if (stateName == "ACTIVE") CallRecived();
          if (SipekResources.CallManager.Is3Pty) stateName = "CONFERENCE";
          ListViewItem lvi = new ListViewItem(new string[] {
            stateName, display, duration});

          lvi.Tag = kvp.Value;
          listViewCallLines.Items.Add(lvi);
          lvi.Selected = true;

          // display info
          //toolStripStatusLabel1.Text = item.Value.lastInfoMessage;
        }


        if (callList.Count > 0)
        {
          // control refresh timer
          tmr.Start();

          // Remember last status
          if (toolStripComboBoxUserStatus.SelectedIndex != (int)EUserStatus.OTP) 
            _lastUserStatus = (EUserStatus)toolStripComboBoxUserStatus.SelectedIndex;

          // Set user status "On the Phone"
          toolStripComboBoxUserStatus.SelectedIndex = (int)EUserStatus.OTP;
        }
        else
        {
          toolStripComboBoxUserStatus.SelectedIndex = (int)_lastUserStatus;
        }

      }
      catch (Exception e)
      {
        // TODO!!!!!!!!!!! Sychronize SHARED RESOURCES!!!!
      }
      //listViewCallLines.AutoResizeColumns(ColumnHeaderAutoResizeStyle.ColumnContent);
    }

      private void CallRecived()
      {
         
      }

    public void UpdateCallTimeout(object sender, EventArgs e)
    {
      if (listViewCallLines.Items.Count == 0) return;

      for (int i = 0; i < listViewCallLines.Items.Count; i++ )
      {
        ListViewItem item = listViewCallLines.Items[i];
        IStateMachine sm = (IStateMachine)item.Tag;
        if (sm.IsNull) continue;

        string duration = sm.RuntimeDuration.ToString();
        if (duration.IndexOf('.') > 0) duration = duration.Remove(duration.IndexOf('.')); // remove miliseconds

        item.SubItems[2].Text = duration;
      }
      // restart timer
      if (listViewCallLines.Items.Count > 0)
      {
        tmr.Start();
      }

    }



    private void toolStripButtonHoldRetrieve_Click(object sender, EventArgs e)
    {
      if (listViewCallLines.SelectedItems.Count > 0)
      {
        ListViewItem lvi = listViewCallLines.SelectedItems[0];

        SipekResources.CallManager.onUserHoldRetrieve(((CStateMachine)lvi.Tag).Session);
      }
    }

    private void toolStripButtonCall_Click(object sender, EventArgs e)
    {
      // TODO check if incoming call!!!
      if (listViewCallLines.SelectedItems.Count > 0)
      {
        ListViewItem lvi = listViewCallLines.SelectedItems[0];
        CStateMachine call = (CStateMachine)lvi.Tag;
        if (call.Incoming) 
        {
          SipekResources.CallManager.onUserAnswer(call.Session);
          return;
        }
      }
      if (toolStripComboDial.Text.Length > 0)
      {
        SipekResources.CallManager.createOutboundCall(toolStripComboDial.Text);
      }
    }

    private void releaseToolStripMenuItem_Click(object sender, EventArgs e)
    {
      if (listViewCallLines.SelectedItems.Count > 0)
      {
        ListViewItem lvi = listViewCallLines.SelectedItems[0];
        SipekResources.CallManager.onUserRelease(((CStateMachine)lvi.Tag).Session);
      }
    }

    private void toolStripComboDial_KeyDown(object sender, KeyEventArgs e)
    {
      if (e.KeyValue == 0x0d)
      {
        if (toolStripComboDial.Text.Length > 0)
        {
          SipekResources.CallManager.createOutboundCall(toolStripComboDial.Text);
        }
      }
    }

    private void listViewCallRegister_DoubleClick(object sender, EventArgs e)
    {
      if (listViewCallRegister.SelectedItems.Count > 0)
      {
        ListViewItem lvi = listViewCallRegister.SelectedItems[0];
        CCallRecord record = (CCallRecord)lvi.Tag;
        SipekResources.CallManager.createOutboundCall(record.Number);
      }
    }

    private void acceptToolStripMenuItem_Click(object sender, EventArgs e)
    {
      if (listViewCallLines.SelectedItems.Count > 0)
      {
        ListViewItem lvi = listViewCallLines.SelectedItems[0];
        SipekResources.CallManager.onUserAnswer(((CStateMachine)lvi.Tag).Session);
      }
    }

    #endregion

    private void removeToolStripMenuItem_Click(object sender, EventArgs e)
    {
      if (listViewCallRegister.SelectedItems.Count > 0)
      {
        ListViewItem lvi = listViewCallRegister.SelectedItems[0];
        CCallRecord record = (CCallRecord) lvi.Tag;
        SipekResources.CallLogger.deleteRecord(record);
      }
      this.UpdateCallRegister();

    }

    private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
    {
      if (IsInitialized)
      {
        SipekResources.CallLogger.save();
      }
      SipekResources.Configurator.Save();
      // shutdown stack
      SipekResources.CallManager.Shutdown();

      if (comPort != null)
      {
          if (comPort.IsOpen == true) comPort.Close();
          comPort.Dispose();
          comPort = null;
      }
    }

    private void toolStripTextBoxTransferTo_KeyDown(object sender, KeyEventArgs e)
    {
      if (e.KeyValue == 0x0d)
      {
        if (listViewCallLines.SelectedItems.Count > 0)
        {
          ListViewItem lvi = listViewCallLines.SelectedItems[0];
          if (toolStripTextBoxTransferTo.Text.Length > 0)
          {
            SipekResources.CallManager.onUserTransfer(((CStateMachine)lvi.Tag).Session, toolStripTextBoxTransferTo.Text);
          }
        }
        contextMenuStripCalls.Close();
      }
    }

    private void toolStripButtonDND_Click(object sender, EventArgs e)
    {
      SipekResources.Configurator.DNDFlag = toolStripButtonDND.Checked;
    }

    private void toolStripButtonAA_Click(object sender, EventArgs e)
    {
      SipekResources.Configurator.AAFlag = toolStripButtonAA.Checked;
    }


    private void toolStripComboBoxUserStatus_SelectedIndexChanged(object sender, EventArgs e)
    {
      /*
       * AVAILABLE, BUSY, OTP, IDLE, AWAY, BRB, OFFLINE
       * 
      Available
      Busy
      On the Phone
      Idle
      Away
      Be Right Back
      Offline
       */

      EUserStatus status = (EUserStatus)toolStripComboBoxUserStatus.SelectedIndex;

      SipekResources.Messenger.setStatus(SipekResources.Configurator.DefaultAccountIndex, status);
    }


      /*
    public void onUserDialDigit(string digits)
    {
      if (listViewCallLines.SelectedItems.Count > 0)
      {
        ListViewItem lvi = listViewCallLines.SelectedItems[0];
        SipekResources.CallManager.onUserDialDigit(((CStateMachine)lvi.Tag).Session, digits, SipekResources.Configurator.DtmfMode);
      }
    }
       * */
    
    /////////////////////////////////////////////////////////////////////////////////////////
    /// Audio Control

    private Mixers mMixers;
    private bool mAvoidEvents = false;

    private void LoadAudioValues()
    {
			try {
      mMixers = new Mixers();
			} catch (Exception e)
      {
        ///report error
          MessageBox.Show("Audio Mixer cannot initialize! \r\nCheck audio configuration and start again!");
        return;
      }
      // set callback
      mMixers.Playback.MixerLineChanged += new WaveLib.AudioMixer.Mixer.MixerLineChangeHandler(mMixer_MixerLineChanged);
      mMixers.Recording.MixerLineChanged += new WaveLib.AudioMixer.Mixer.MixerLineChangeHandler(mMixer_MixerLineChanged);

      MixerLine pbline = mMixers.Playback.UserLines.GetMixerFirstLineByComponentType(MIXERLINE_COMPONENTTYPE.SRC_WAVEOUT);

      toolStripTrackBar1.Tag = pbline;
      toolStripMuteButton.Tag = pbline;
      MixerLine recline = mMixers.Recording.UserLines.GetMixerFirstLineByComponentType(MIXERLINE_COMPONENTTYPE.SRC_MICROPHONE); ;
      toolStripMicMuteButton.Tag = recline;

      //If it is 2 channels then ask both and set the volume to the bigger but keep relation between them (Balance)
      int volume = 0;
      float balance = 0;
      if (pbline.Channels != 2)
        volume = pbline.Volume;
      else
      {
        pbline.Channel = Channel.Left;
        int left = pbline.Volume;
        pbline.Channel = Channel.Right;
        int right = pbline.Volume;
        if (left > right)
        {
          volume = left;
          balance = (volume > 0) ? -(1 - (right / (float)left)) : 0;
        }
        else
        {
          volume = right;
          balance = (volume > 0) ? (1 - (left / (float)right)) : 0;
        }
      }

      if (volume >= 0)
        this.toolStripTrackBar1.Value = volume;
      else
        this.toolStripTrackBar1.Enabled = false;

      // toolstrip checkboxes
      this.toolStripMuteButton.Checked = pbline.Mute;
      this.toolStripMicMuteButton.Checked = recline.Volume == 0 ? true : false;
      _lastMicVol = recline.Volume;
    }

    /// <summary>
    /// Callback from Windows Volume Control
    /// </summary>
    /// <param name="mixer"></param>
    /// <param name="line"></param>
    private void mMixer_MixerLineChanged(Mixer mixer, MixerLine line)
    {
      mAvoidEvents = true;

      try
      {
        float balance = -1;
        MixerLine frontEndLine = (MixerLine)toolStripTrackBar1.Tag;
        if (frontEndLine == line)
        {
          int volume = 0;
          if (line.Channels != 2)
            volume = line.Volume;
          else
          {
            line.Channel = Channel.Left;
            int left = line.Volume;
            line.Channel = Channel.Right;
            int right = line.Volume;
            if (left > right)
            {
              volume = left;
              // TIP: Do not reset the balance if both left and right channel have 0 value
              if (left != 0 && right != 0)
                balance = (volume > 0) ? -(1 - (right / (float)left)) : 0;
            }
            else
            {
              volume = right;
              // TIP: Do not reset the balance if both left and right channel have 0 value
              if (left != 0 && right != 0)
                balance = (volume > 0) ? 1 - (left / (float)right) : 0;
            }
          }

          if (volume >= 0)
            toolStripTrackBar1.Value = volume;

        }

        // adjust toolstrip checkboxes
        if ((MixerLine)toolStripMicMuteButton.Tag == line)
        {
          toolStripMicMuteButton.Checked = line.Volume == 0 ? true : false;
        }
        else if ((MixerLine)toolStripMuteButton.Tag == line)
        {
           toolStripMuteButton.Checked = line.Mute;
        }
      }
      finally
      {
        mAvoidEvents = false;
      }
    }

    private void toolStripTrackBar1_ValueChanged(object sender, EventArgs e)
    {
      if (mAvoidEvents)
        return;

      TrackBar tBar = (TrackBar)sender;
      MixerLine line = (MixerLine)tBar.Tag;
      if (line.Channels != 2)
      {
        // One channel or more than two let set the volume uniform
        line.Channel = Channel.Uniform;
        line.Volume = tBar.Value;
      }
      else
      {
        //Set independent volume
        line.Channel = Channel.Uniform;
        line.Volume = toolStripTrackBar1.Value;
      }
    }

    private int _lastMicVol = 0;

    private void toolStripMuteButton_Click(object sender, EventArgs e)
    {
      ToolStripButton chkBox = (ToolStripButton)sender;
      MixerLine line = (MixerLine)chkBox.Tag;
      if (line.Direction == MixerType.Recording)
      {
        //line.Selected = chkBox.Checked;
        if (chkBox.Checked == true)
        {
          _lastMicVol = line.Volume;
          line.Volume = 0;
        }
        else 
        {
          line.Volume = _lastMicVol;
        }
      }
      else
      {
        line.Mute = chkBox.Checked;
      }
    }

    private void MainForm_Load(object sender, EventArgs e)
    {
      // Init com port - Modem
      comPort = new System.IO.Ports.SerialPort();
      comPort.DataReceived += new System.IO.Ports.SerialDataReceivedEventHandler(comPort_DataReceived);
      comPort.ErrorReceived += new System.IO.Ports.SerialErrorReceivedEventHandler(comPort_ErrorReceived);

      LoadAudioValues();

      RegState();
      // Register callbacks from callcontrol
      SipekResources.CallManager.CallStateRefresh += onCallStateChanged;
      // Register callbacks from pjsipWrapper
      //SipekFactory.getCommonProxy().CallStateChanged += onTelephonyRefresh;
      SipekResources.Registrar.AccountStateChanged += onAccountStateChanged;

      // Initialize and set factory for CallManager
      
      int status = SipekResources.CallManager.Initialize();
      SipekResources.CallManager.CallLogger = SipekResources.CallLogger;

      if (status != 0)
      {
          MessageBox.Show("Init SIP stack problem! \r\nPlease, check configuration and start again! \r\nStatus code " + status);
        return;
      }

      // initialize Stack
      SipekResources.Registrar.registerAccounts();
      SipekResources.Configurator.AAFlag = true; // Stavimod a se automatski javlja


      //////////////////////////////////////////////////////////////////////////
      // load settings

      this.UpdateCallRegister();

      // Set user status
      toolStripComboBoxUserStatus.SelectedIndex = (int)EUserStatus.AVAILABLE;

      // scoh::::03.04.2008:::pjsip ISSUE??? At startup codeclist is different as later 
      // set codecs priority...
      // initialize/reset codecs - enable PCMU and PCMA only
      int noOfCodecs = SipekResources.StackProxy.getNoOfCodecs();
      for (int i = 0; i < noOfCodecs; i++)
      {
        string codecname = SipekResources.StackProxy.getCodec(i);
        if (SipekResources.Configurator.CodecList.Contains(codecname))
        {
          // leave default
          SipekResources.StackProxy.setCodecPriority(codecname, 128);
        }
        else
        {
          // disable
          SipekResources.StackProxy.setCodecPriority(codecname, 0);
        }
      }

      // timer 
      tmr.Interval = 1000;
      tmr.Tick += new EventHandler(UpdateCallTimeout);
    }

      private void RegState()
      {
          for (int i = 0; i < SipekResources.Configurator.Accounts.Count; i++)
          {
              IAccount acc = SipekResources.Configurator.Accounts[i];
              string name;

              if (acc.AccountName.Length == 0)
              {
                  name = "--empty--";
              }
              else
              {
                  name = acc.AccountName;
              }
              // create listviewitem
              ListViewItem item = new ListViewItem(new string[] { name, acc.RegState.ToString() });
              // mark default account
              if (i == SipekResources.Configurator.DefaultAccountIndex)
              {
                  // Mark default account; todo!!! Coloring!
                  item.BackColor = Color.LightGray;

                  string label = "";
                  // check registration status
                  if (acc.RegState == 200)
                  {
                      this.Text = HEADER_TEXT + " - " + acc.AccountName + " (" + acc.DisplayName + ")"; ;
                      label = "Registered" + " - " + acc.AccountName + " (" + acc.DisplayName + ")";
                  }
                  else if (acc.RegState == 0)
                  {
                      label = "Trying..." + " - " + acc.AccountName;
                  }
                  else
                  {
                      label = "Not registered" + " - " + acc.AccountName;
                  }
                  this.Text = "voicePort :: " + label;
              }
              else
              {
              }
          }
      }

    private void listViewAccounts_DoubleClick(object sender, EventArgs e)
    {
      SettingsForm sf = new SettingsForm(this.SipekResources);
      //sf.activateTab("");
      sf.ShowDialog();
    }

    private void unconditionalToolStripMenuItem_Click(object sender, EventArgs e)
    {
      SipekResources.Configurator.CFUFlag = unconditionalToolStripMenuItem.Checked;
    }

    private void noReplyToolStripMenuItem_Click(object sender, EventArgs e)
    {
      SipekResources.Configurator.CFNRFlag = noReplyToolStripMenuItem.Checked;
    }

    private void busyToolStripMenuItem_Click(object sender, EventArgs e)
    {
      SipekResources.Configurator.CFBFlag = busyToolStripMenuItem.Checked;
    }

    private void toolStripTextBoxCFUNumber_TextChanged(object sender, EventArgs e)
    {
      SipekResources.Configurator.CFUNumber = toolStripTextBoxCFUNumber.Text;
    }

    private void toolStripTextBoxCFNRNumber_TextChanged(object sender, EventArgs e)
    {
      SipekResources.Configurator.CFNRNumber = toolStripTextBoxCFNRNumber.Text;
    }

    private void toolStripTextBoxCFBNumber_TextChanged(object sender, EventArgs e)
    {
      SipekResources.Configurator.CFBNumber = toolStripTextBoxCFBNumber.Text;
    }

    private void toolStrip3PtyButton_Click(object sender, EventArgs e)
    {
      if (listViewCallLines.SelectedItems.Count > 0)
      {
        ListViewItem lvi = listViewCallLines.SelectedItems[0];
        // TODO implement 3Pty
        SipekResources.CallManager.onUserConference(((CStateMachine)lvi.Tag).Session);
      }
    }

    private void MainForm_Activated(object sender, EventArgs e)
    {
    }



      private void splitContainerEW_Panel2_Paint(object sender, PaintEventArgs e)
      {

      }

      private void toolStripContainer2_TopToolStripPanel_Click(object sender, EventArgs e)
      {

      }

      private void toolStripButton1_Click(object sender, EventArgs e)
      {
          (new SettingsForm(this.SipekResources)).ShowDialog();
          RefreshForm();
      }

      public void WAIT4OK()
      {
          do
          {
          } while (isOK);
          isOK = false;
      }

      private void openModem_Click(object sender, EventArgs e)
      {
          comPort.PortName = "COM3";
          // currPort1.BreakState = True
          comPort.Close();
          //currPort1.WriteBufferSize = 1
          comPort.Parity = System.IO.Ports.Parity.None;
          comPort.BaudRate = 115200;
          comPort.DataBits = 8;
          comPort.Handshake = System.IO.Ports.Handshake.None;
          comPort.RtsEnable = true;
          comPort.DtrEnable = true;
          
         try
         {
          comPort.Open();
         // Debug("COM port is ready.");
         } catch { 
             MessageBox.Show("Error opening the COM port."); 
         }


          comPort.Write("AT" + "\r\n"); //WAKEUP
          WAIT4OK();
          

          comPort.Write("AT+NREC=1" + "\r\n"); //enable noise reduction
          WAIT4OK();

          comPort.Write("AT*EPHD=1" + "\r\n"); //Inform the phone the bluetooth HF is connected
          WAIT4OK();

          comPort.Write("AT*EAPM=2" + "\r\n"); //Rout the audio to the bluetooth
          WAIT4OK();

          comPort.Write("AT*ECAM=1" + "\r\n"); //MONITOR CALL STATUS
          WAIT4OK();
      }

      public void comPort_DataReceived(Object sender, System.IO.Ports.SerialDataReceivedEventArgs e)
      {
          String recData = comPort.ReadExisting();
          Console.Write(">>>>>>>>>>>>>>>>>>>>>>>>>>>>" + recData + "\n\r");
          if (recData.Contains("OK"))
          {
              //sipXtapi.Interop.sipXtapi.sipxCallAnswer(CurrCall, True)
              isOK = true;
          }

         // if (recData.Contains("BUSY") || recData.Contains("ERROR") || recData.Contains("NO DIALTONE"))
            //  sipXtapi.Interop.sipXtapi.sipxCallReject(CurrCall, 400, "BUSY HERE");

          if (recData.Contains("*ECAV:"))
          {
              int cSTART;
              cSTART = recData.IndexOf("*ECAV:") + 8;
              int CCSTATUS;
              CCSTATUS = Convert.ToInt32(recData.Substring(cSTART, 1));

              switch (CCSTATUS)
              {
                  case 0: //IDLE
                    //  sipXtapi.Interop.sipXtapi.sipxCallDestroy(ref CurrCall);
                      break;
                  case 1: break;//CALLING
                  case 2: break;//CONNECTING
                  case 3: break;//ACTIVE
                    //  sipXtapi.Interop.sipXtapi.sipxCallAnswer(CurrCall, true);
                      break;
                  case 4: //HOLD
                   //   sipXtapi.Interop.sipXtapi.sipxCallHold(CurrCall, true);
                      break;
                  case 5: //WAITING
                      break;
                  case 6: //ALERTING
                      break;
                  case 7: //BUSY
                     // sipXtapi.Interop.sipXtapi.sipxCallReject(CurrCall, 400, "IDLE");
                      break;
              }

          }

      }

      public void comPort_ErrorReceived(Object sender, System.IO.Ports.SerialErrorReceivedEventArgs e)
      {

          MessageBox.Show(">ERR>>>>>>>>>>>>>>>>>>>>>>>>>>>" + comPort.ReadExisting() + "\n\r");
       //   sipXtapi.Interop.sipXtapi.sipxCallReject(CurrCall, 400, "BUSY HERE");
      }

  }




  //[System.ComponentModel.DesignerCategory("code")]
  [ToolStripItemDesignerAvailability(ToolStripItemDesignerAvailability.ToolStrip | ToolStripItemDesignerAvailability.StatusStrip)]
  public partial class ToolStripTrackBar : ToolStripControlHost
  {
    public ToolStripTrackBar()
      : base(CreateControlInstance())
    {

    }

    /// <summary>
    /// Create a strongly typed property called TrackBar - handy to prevent casting everywhere.
    /// </summary>
    public TrackBar TrackBar
    {
      get
      {
        return Control as TrackBar;
      }
    }

    /// <summary>
    /// Create the actual control, note this is static so it can be called from the
    /// constructor.
    ///
    /// </summary>
    /// <returns></returns>
    private static Control CreateControlInstance()
    {
      TrackBar t = new TrackBar();
      t.AutoSize = false;
      t.Height = 16;
      t.TickFrequency = 6553;
      t.SmallChange = 6553;
      t.LargeChange = 10000;
      t.Minimum = 0;
      t.Maximum = 65535;

      // Add other initialization code here.
      return t;
    }

    [DefaultValue(0)]
    public int Value
    {
      get { return TrackBar.Value; }
      set { TrackBar.Value = value; }
    }
    
    [DefaultValue(0)]
    public new object Tag
    {
      get { return TrackBar.Tag; }
      set { TrackBar.Tag = value; }
    }

    /// <summary>
    /// Attach to events we want to re-wrap
    /// </summary>
    /// <param name="control"></param>
    protected override void OnSubscribeControlEvents(Control control)
    {
      base.OnSubscribeControlEvents(control);
      TrackBar trackBar = control as TrackBar;
      trackBar.ValueChanged += new EventHandler(trackBar_ValueChanged);
    }

    /// <summary>
    /// Detach from events.
    /// </summary>
    /// <param name="control"></param>
    protected override void OnUnsubscribeControlEvents(Control control)
    {
      base.OnUnsubscribeControlEvents(control);
      TrackBar trackBar = control as TrackBar;
      trackBar.ValueChanged -= new EventHandler(trackBar_ValueChanged);

    }


    /// <summary>
    /// Routing for event
    /// TrackBar.ValueChanged -> ToolStripTrackBar.ValueChanged
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    void trackBar_ValueChanged(object sender, EventArgs e)
    {
      // when the trackbar value changes, fire an event.
      if (this.ValueChanged != null)
      {
        ValueChanged(sender, e);
      }
    }

    // add an event that is subscribable from the designer.
    public event EventHandler ValueChanged;


    // set other defaults that are interesting
    protected override Size DefaultSize
    {
      get
      {
        return new Size(200, 16);
      }
    }

  }
}