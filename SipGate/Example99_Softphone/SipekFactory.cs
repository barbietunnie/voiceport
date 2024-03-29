/* 
 * Copyright (C) 2008 Sasa Coh <sasacoh@gmail.com>
 *
 * This program is free software; you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation; either version 2 of the License, or
 * (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
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
using System.Text;
using System.Timers;
using System.Runtime.InteropServices;
using System.Media;
using Sipek.Common;
using Sipek.Common.CallControl;
using Sipek.Sip;


namespace Sipek
{
  /// <summary>
  /// ConcreteFactory 
  /// Implementation of AbstractFactory. 
  /// </summary>
  public class SipekResources : AbstractFactory
  {
    MainForm _form; // reference to MainForm to provide timer context
    IMediaProxyInterface _mediaProxy = new CMediaPlayerProxy();
    ICallLogInterface _callLogger = new CCallLog();
    pjsipStackProxy _stackProxy = pjsipStackProxy.Instance;
    SipekConfigurator _config = new SipekConfigurator();

    #region Constructor
    public SipekResources(MainForm mf)
    {
      _form = mf;

      // initialize sip struct at startup
      SipConfigStruct.Instance.stunServer = this.Configurator.StunServerAddress;
      SipConfigStruct.Instance.publishEnabled = this.Configurator.PublishEnabled;
     // SipConfigStruct.Instance.expires = this.Configurator.Expires;

      // initialize modules
      _callManager.StackProxy = _stackProxy;
      _callManager.Config = _config;
      _callManager.Factory = this;
      _callManager.MediaProxy = _mediaProxy;
      _stackProxy.Config = _config;
      _registrar.Config = _config;
      _messenger.Config = _config;

      // do not save account state
      for (int i = 0; i < 5; i++)
      {
        Properties.Settings.Default.cfgSipAccountState[i] = "0";
        Properties.Settings.Default.cfgSipAccountIndex[i] = "0";
      }
    }
    #endregion Constructor

    #region AbstractFactory methods
    public ITimer createTimer()
    {
      return new GUITimer(_form);
    }

    public IStateMachine createStateMachine()
    {
      // TODO: check max number of calls
      return new CStateMachine();
    }

    #endregion

    #region Other Resources
    public pjsipStackProxy StackProxy
    {
      get { return _stackProxy; }
      set { _stackProxy = value; }
    }

    public SipekConfigurator Configurator
    {
      get { return _config; }
      set {}
    }

    // getters
    public IMediaProxyInterface MediaProxy
    {
      get { return _mediaProxy; }
      set { }
    }

    public ICallLogInterface CallLogger
    {
      get { return _callLogger; }
      set { }
    }

    private IRegistrar _registrar = pjsipRegistrar.Instance;
    public IRegistrar Registrar
    {
      get { return _registrar; }
    }

    private IPresenceAndMessaging _messenger = pjsipPresenceAndMessaging.Instance;
    public IPresenceAndMessaging Messenger
    {
      get { return _messenger; }
    }

    private CCallManager _callManager = CCallManager.Instance;
    public CCallManager CallManager
    {
      get { return CCallManager.Instance; }
    }
    #endregion
  }

  #region Concrete implementations

  public class GUITimer : ITimer
  {
    Timer _guiTimer;
    MainForm _form;


    public GUITimer(MainForm mf)
    {
      _form = mf;
      _guiTimer = new Timer();
      if (this.Interval > 0) _guiTimer.Interval = this.Interval;
      _guiTimer.Interval = 100;
      _guiTimer.Enabled = true;
      _guiTimer.Elapsed += new ElapsedEventHandler(_guiTimer_Tick);
    }

    void _guiTimer_Tick(object sender, EventArgs e)
    {
      _guiTimer.Stop();
      //_elapsed(sender, e);
      // Synchronize thread with GUI because SIP stack works with GUI thread only
      if (!_form.Disposing)
        _form.Invoke(_elapsed, new object[] { sender, e});
    }

    public bool Start()
    {
      _guiTimer.Start();
      return true;
    }

    public bool Stop()
    {
      _guiTimer.Stop();
      return true;
    }

    private int _interval;
    public int Interval
    {
      get { return _interval; }
      set { _interval = value; _guiTimer.Interval = value; }
    }

    private TimerExpiredCallback _elapsed;
    public TimerExpiredCallback Elapsed
    {
      set { 
        _elapsed = value;
      }
    }
  }


  // Accounts
  public class SipekAccount : IAccount
  {
    private int _index = -1;
    private int _accountIdentification = -1;
    
    /// <summary>
    /// Temp storage!
    /// The account index assigned by voip stack
    /// </summary>
    public int Index
    {
      get
      { 
        int value;
        if (Int32.TryParse(Properties.Settings.Default.cfgSipAccountIndex[_index], out value))
        {
          return value;
        }
        return -1; 
      }
      set { Properties.Settings.Default.cfgSipAccountIndex[_index] = value.ToString(); }
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="index">the account identification used by configuration (values 0..4)</param>
    public SipekAccount(int index)
    {
      _index = index;
    }

    #region Properties

    public string AccountName
    {
      get
      {
        return Properties.Settings.Default.cfgSipAccountNames[_index];
      }
      set
      {
        Properties.Settings.Default.cfgSipAccountNames[_index] = value;
      }
    }

    public string HostName
    {
      get
      {
        return Properties.Settings.Default.cfgSipAccountAddresses[_index];
      }
      set
      {
        Properties.Settings.Default.cfgSipAccountAddresses[_index] = value;
      }
    }

    public string Id
    {
      get
      {
        return Properties.Settings.Default.cfgSipAccountIds[_index];
      }
      set
      {
        Properties.Settings.Default.cfgSipAccountIds[_index] = value;
      }
    }

    public string UserName
    {
      get
      {
        return Properties.Settings.Default.cfgSipAccountUsername[_index];
      }
      set
      {
        Properties.Settings.Default.cfgSipAccountUsername[_index] = value;
      }
    }

    public string Password
    {
      get
      {
        return Properties.Settings.Default.cfgSipAccountPassword[_index];
      }
      set
      {
        Properties.Settings.Default.cfgSipAccountPassword[_index] = value;
      }
    }

    public string DisplayName
    {
      get
      {
        return Properties.Settings.Default.cfgSipAccountDisplayName[_index];
      }
      set
      {
        Properties.Settings.Default.cfgSipAccountDisplayName[_index] = value;
      }
    }

    public string DomainName
    {
      get
      {
        return Properties.Settings.Default.cfgSipAccountDomains[_index];
      }
      set
      {
        Properties.Settings.Default.cfgSipAccountDomains[_index] = value;
      }
    }

    public int RegState
    {
      get 
      {
        int value;
        if (Int32.TryParse(Properties.Settings.Default.cfgSipAccountState[_index], out value))
        {
          return value;
        }
        return 0; 
      }
      set
      {
        Properties.Settings.Default.cfgSipAccountState[_index] = value.ToString();
      }
    }

    public string ProxyAddress
    {
      get
      {
        return Properties.Settings.Default.cfgSipAccountProxyAddresses[_index];
      }
      set
      {
        Properties.Settings.Default.cfgSipAccountProxyAddresses[_index] = value;
      }
    }

    public ETransportMode TransportMode
    {
      get
      {
        int value;
        if (Int32.TryParse(Properties.Settings.Default.cfgSipAccountTransport[_index], out value))
        {
          return (ETransportMode)value;
        }
        return (ETransportMode.TM_UDP); // default
      }
      set
      {
        Properties.Settings.Default.cfgSipAccountTransport[_index] = ((int)value).ToString();
      }
    }
    #endregion

  }

  /// <summary>
  /// 
  /// </summary>
  public class SipekConfigurator : IConfiguratorInterface
  {
    public bool IsNull { get { return false; } }

    public bool CFUFlag {
      get { return Properties.Settings.Default.cfgCFUFlag; }
      set { Properties.Settings.Default.cfgCFUFlag = value; }
    }
    public string CFUNumber 
    {
      get { return Properties.Settings.Default.cfgCFUNumber; }
      set { Properties.Settings.Default.cfgCFUNumber = value; }
    }
    public bool CFNRFlag 
    {
      get { return Properties.Settings.Default.cfgCFNRFlag; }
      set { Properties.Settings.Default.cfgCFNRFlag = value; }
    }
    public string CFNRNumber 
    {
      get { return Properties.Settings.Default.cfgCFNRNumber; }
      set { Properties.Settings.Default.cfgCFNRNumber = value; }
    }
    public bool DNDFlag {
      get { return Properties.Settings.Default.cfgDNDFlag; }
      set { Properties.Settings.Default.cfgDNDFlag = value; }
    }
    public bool AAFlag {
      get { return Properties.Settings.Default.cfgAAFlag; }
      set { Properties.Settings.Default.cfgAAFlag = value; }
    }

    public bool CFBFlag
    {
      get { return Properties.Settings.Default.cfgCFBFlag; }
      set { Properties.Settings.Default.cfgCFBFlag = value; }
    }

    public string CFBNumber
    {
      get { return Properties.Settings.Default.cfgCFBNumber; }
      set { Properties.Settings.Default.cfgCFBNumber = value; }
    }

    public int SIPPort
    {
      get { return Properties.Settings.Default.cfgSipPort; }
      set { Properties.Settings.Default.cfgSipPort = value; }
    }

    public bool PublishEnabled
    {
      get {
        SipConfigStruct.Instance.publishEnabled = Properties.Settings.Default.cfgSipPublishEnabled;
        return Properties.Settings.Default.cfgSipPublishEnabled;
      }
      set {
        SipConfigStruct.Instance.publishEnabled = value;
        Properties.Settings.Default.cfgSipPublishEnabled = value;
      }    
    }

    public string StunServerAddress
    {
      get
      {
        SipConfigStruct.Instance.stunServer = Properties.Settings.Default.cfgStunServerAddress;
        return Properties.Settings.Default.cfgStunServerAddress;
      }
      set
      {
        Properties.Settings.Default.cfgStunServerAddress = value;
        SipConfigStruct.Instance.stunServer = value;
      }
    }

    public EDtmfMode DtmfMode
    {
      get
      {
        return (EDtmfMode)Properties.Settings.Default.cfgDtmfMode;
      }
      set
      {
        Properties.Settings.Default.cfgDtmfMode = (int)value;
      }
    }

    public int Expires
    {
      get
      {
       // SipConfigStruct.Instance.expires = Properties.Settings.Default.cfgRegistrationTimeout;
        return Properties.Settings.Default.cfgRegistrationTimeout;
      }
      set
      {
        Properties.Settings.Default.cfgRegistrationTimeout = value;
      //  SipConfigStruct.Instance.expires = value;
      }
    }

    /// <summary>
    /// The position of default account in account list. Does NOT mean same as DefaultAccountIndex
    /// </summary>
    public int DefaultAccountIndex
    {
      get
      {
        return Properties.Settings.Default.cfgSipAccountDefault;
      }
      set
      {
        Properties.Settings.Default.cfgSipAccountDefault = value;
      }
    }

    public List<IAccount> Accounts
    {
      get 
      {
        List<IAccount> accList = new List<IAccount>();
        for (int i=0; i<5; i++)
        {
          IAccount item = new SipekAccount(i);
          accList.Add(item);
    }
        return accList; 

    }
    }

    public void Save()
    {
      // save properties
      Properties.Settings.Default.Save();
    }

    public List<string> CodecList
    {
      get 
      {
        List<string> codecList = new List<string>();
        foreach (string item in Properties.Settings.Default.cfgCodecList)
        {
          codecList.Add(item);
        }
        return codecList; 
      }
      set 
      {
        Properties.Settings.Default.cfgCodecList.Clear();
        List<string> cl = value;
        foreach (string item in cl)
        {
          Properties.Settings.Default.cfgCodecList.Add(item);
        }
      }
    }
  }


  //////////////////////////////////////////////////////
  // Media proxy
  // internal class
  public class CMediaPlayerProxy : IMediaProxyInterface
  {
    SoundPlayer player = new SoundPlayer();

    #region Methods

    public int playTone(ETones toneId)
    {
      string fname;

      switch (toneId)
      {
        case ETones.EToneDial:
          fname = "Sounds/dial.wav";
          break;
        case ETones.EToneCongestion:
          fname = "Sounds/congestion.wav";
          break;
        case ETones.EToneRingback:
          fname = "Sounds/ringback.wav";
          break;
        case ETones.EToneRing:
          fname = "Sounds/ring.wav";
          break;
        default:
          fname = "";
          break;
      }

      player.SoundLocation = fname;
      player.Load();
      player.PlayLooping();

      return 1;
    }

    public int stopTone()
    {
      player.Stop();
      return 1;
    }

    #endregion

  }

  #endregion Concrete Implementations

}
