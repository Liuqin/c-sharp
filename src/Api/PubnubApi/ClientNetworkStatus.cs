﻿using System;
using System.Threading;
#if NETFX_CORE
using Windows.Networking.Sockets;
using Windows.Networking;
using Windows.Foundation;
#else
using System.Net.Sockets;
#endif
using System.Net;

namespace PubnubApi
{
	#region "Network Status -- code split required"
	internal abstract class ClientNetworkStatus
	{
		private static bool _status = true;
		private static bool _failClientNetworkForTesting = false;
		private static bool _machineSuspendMode = false;

		private static IJsonPluggableLibrary _jsonPluggableLibrary;
		internal static IJsonPluggableLibrary JsonPluggableLibrary
		{
			get
			{
				return _jsonPluggableLibrary;
			}
			set
			{
				_jsonPluggableLibrary = value;
			}
		}

		#if (SILVERLIGHT  || WINDOWS_PHONE)
		private static ManualResetEvent mres = new ManualResetEvent(false);
		private static ManualResetEvent mreSocketAsync = new ManualResetEvent(false);
		#else
		private static ManualResetEventSlim mres = new ManualResetEventSlim(false);
		#endif
		internal static bool SimulateNetworkFailForTesting
		{
			get
			{
				return _failClientNetworkForTesting;
			}

			set
			{
				_failClientNetworkForTesting = value;
			}

		}

		internal static bool MachineSuspendMode
		{
			get
			{
				return _machineSuspendMode;
			}
			set
			{
				_machineSuspendMode = value;
			}
		}
		#if(__MonoCS__)
		static UdpClient udp;
		#endif

		#if(__MonoCS__)
		static HttpWebRequest request;
		static WebResponse response;
		internal static int HeartbeatInterval {
			get;
			set;
		}
		internal static bool CheckInternetStatusUnity<T>(bool systemActive, Action<PubnubClientError> errorCallback, string[] channels, int heartBeatInterval)
		{
			HeartbeatInterval = heartBeatInterval;
			if (_failClientNetworkForTesting)
			{
				//Only to simulate network fail
				return false;
			}
			else
			{
				CheckClientNetworkAvailability<T>(CallbackClientNetworkStatus, errorCallback, channels);
				return _status;
			}
		}
		#endif
		internal static bool CheckInternetStatus(bool systemActive, Action<PubnubClientError> errorCallback, string[] channels, string[] channelGroups)
		{
			if (_failClientNetworkForTesting || _machineSuspendMode)
			{
				//Only to simulate network fail
				return false;
			}
			else
			{
                CheckClientNetworkAvailability(CallbackClientNetworkStatus, errorCallback, channels, channelGroups);
				return _status;
			}
		}
		//#endif

		public static bool GetInternetStatus()
		{
			return _status;
		}

		private static void CallbackClientNetworkStatus(bool status)
		{
			_status = status;
		}

		private static void CheckClientNetworkAvailability(Action<bool> callback, Action<PubnubClientError> errorCallback, string[] channels, string[] channelGroups)
		{
			InternetState state = new InternetState();
			state.Callback = callback;
			state.ErrorCallback = errorCallback;
			state.Channels = channels;
            state.ChannelGroups = channelGroups;
            #if (NETFX_CORE)
            CheckSocketConnect(state);
			#elif(__MonoCS__)
			CheckSocketConnect(state);
			#else
			ThreadPool.QueueUserWorkItem(CheckSocketConnect, state);
			#endif

			#if (SILVERLIGHT || WINDOWS_PHONE)
			mres.WaitOne();
			#else
			mres.Wait();
			#endif
		}

		private static void CheckSocketConnect(object internetState)
		{
			InternetState state = internetState as InternetState;
			Action<bool> callback = state.Callback;
			Action<PubnubClientError> errorCallback = state.ErrorCallback;
			string[] channels = state.Channels;
            string[] channelGroups = state.ChannelGroups;
			try
			{
				#if (SILVERLIGHT || WINDOWS_PHONE)
				using (Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
				{
					SocketAsyncEventArgs sae = new SocketAsyncEventArgs();
					sae.UserToken = state;
					sae.RemoteEndPoint = new DnsEndPoint("pubsub.pubnub.com", 80);
					sae.Completed += new EventHandler<SocketAsyncEventArgs>(socketAsync_Completed);
					bool test = socket.ConnectAsync(sae);

					mreSocketAsync.WaitOne(1000);
					sae.Completed -= new EventHandler<SocketAsyncEventArgs>(socketAsync_Completed);
					socket.Close();
				}
                #elif NETFX_CORE
                CheckSocketConnectAsync();
				#elif(__MonoCS__)
				udp = new UdpClient("pubsub.pubnub.com", 80);
				IPAddress localAddress = ((IPEndPoint)udp.Client.LocalEndPoint).Address;
				if(udp != null && udp.Client != null){
					EndPoint remotepoint = udp.Client.RemoteEndPoint;
					string remoteAddress = (remotepoint != null) ? remotepoint.ToString() : "";
					LoggingMethod.WriteToLog(string.Format("DateTime {0} checkInternetStatus LocalIP: {1}, RemoteEndPoint:{2}", DateTime.Now.ToString(), localAddress.ToString(), remoteAddress), LoggingMethod.LevelVerbose);
					_status =true;
					callback(true);
				}
				#else
				using (UdpClient udp = new UdpClient("pubsub.pubnub.com", 80))
				{
					IPAddress localAddress = ((IPEndPoint)udp.Client.LocalEndPoint).Address;
					EndPoint remotepoint = udp.Client.RemoteEndPoint;
					string remoteAddress = (remotepoint != null) ? remotepoint.ToString() : "";
					udp.Close();

					LoggingMethod.WriteToLog(string.Format("DateTime {0} checkInternetStatus LocalIP: {1}, RemoteEndPoint:{2}", DateTime.Now.ToString(), localAddress.ToString(), remoteAddress), LoggingMethod.LevelVerbose);
					callback(true);
				}
				#endif
			}
			catch (Exception ex)
			{
				#if(__MonoCS__)
				_status = false;
				#endif
				ParseCheckSocketConnectException(ex, channels, channelGroups, errorCallback, callback);
			}
			finally
			{
				#if(__MonoCS__)
				if(udp!=null){
					udp.Close();
				}
				#endif
			}
			mres.Set();
		}

#if NETFX_CORE
        private static async void CheckSocketConnectAsync()
        {
            try
            {
                DatagramSocket socket = new DatagramSocket();
                await socket.ConnectAsync(new HostName("pubsub.pubnub.com"), "80");
            }
            catch { }
        }
#endif

        static void ParseCheckSocketConnectException(Exception ex, string[] channels, string[] channelGroups, Action<PubnubClientError> errorCallback, Action<bool> callback)
		{
			PubnubErrorCode errorType = PubnubErrorCodeHelper.GetErrorType(ex);
			int statusCode = (int)errorType;
			string errorDescription = PubnubErrorCodeDescription.GetStatusCodeDescription(errorType);
            string channel = (channels == null) ? "" : string.Join(",", channels);
            string channelGroup = (channelGroups == null) ? "" : string.Join(",", channelGroups);
            PubnubClientError error = new PubnubClientError(statusCode, PubnubErrorSeverity.Warn, true, ex.ToString(), ex, PubnubMessageSource.Client, null, null, errorDescription, channel, channelGroup);
			GoToCallback(error, errorCallback);

			LoggingMethod.WriteToLog(string.Format("DateTime {0} checkInternetStatus Error. {1}", DateTime.Now.ToString(), ex.ToString()), LoggingMethod.LevelError);
			callback(false);
		}

		private static void GoToCallback<T>(object result, Action<T> Callback)
		{
			if (Callback != null)
			{
				if (typeof(T) == typeof(string))
				{
					JsonResponseToCallback(result, Callback);
				}
				else
				{
					Callback((T)(object)result);
				}
			}
		}

		private static void GoToCallback<T>(PubnubClientError result, Action<PubnubClientError> Callback)
		{
			if (Callback != null)
			{
				//TODO:
				//Include custom message related to error/status code for developer
				//error.AdditionalMessage = MyCustomErrorMessageRetrieverBasedOnStatusCode(error.StatusCode);

				Callback(result);
			}
		}


		private static void JsonResponseToCallback<T>(object result, Action<T> callback)
		{
			string callbackJson = "";

			if (typeof(T) == typeof(string))
			{
				callbackJson = _jsonPluggableLibrary.SerializeToJsonString(result);

				Action<string> castCallback = callback as Action<string>;
				castCallback(callbackJson);
			}
		}

		#if (SILVERLIGHT || WINDOWS_PHONE)
		static void socketAsync_Completed(object sender, SocketAsyncEventArgs e)
		{
			if (e.LastOperation == SocketAsyncOperation.Connect)
			{
				Socket skt = sender as Socket;
				InternetState state = e.UserToken as InternetState;
				if (state != null)
				{
					LoggingMethod.WriteToLog(string.Format("DateTime {0} socketAsync_Completed.", DateTime.Now.ToString()), LoggingMethod.LevelInfo);
					state.Callback(true);
				}
				mreSocketAsync.Set();
			}
		}
		#endif

	}
	#endregion
}