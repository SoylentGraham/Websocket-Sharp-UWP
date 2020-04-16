#if WINDOWS_UWP
#define CUSTOM_WEBSOCKET
#endif
#if CUSTOM_WEBSOCKET

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using Windows.Networking.Sockets;
using System.Threading.Tasks;
using Windows.Web;

using System;	//	System.Uri
using System.IO;

using Windows.System.Threading;

using System.Runtime.InteropServices.WindowsRuntime;	//	AsBuffer
using Windows.Storage.Streams;			// DataWriter
//	https://github.com/Microsoft/Windows-universal-samples/blob/master/Samples/WebSocket/cs/Scenario2_Binary.xaml.cs


namespace WebSocketSharp
{
	public enum Opcode
	{
		TEXT,
		BINARY
	};

	public class CloseEventArgs : EventArgs
	{
		public ushort Code { get; }
		public string Reason { get; }
		public bool WasClean { get; }
	}

	public class ErrorEventArgs : EventArgs
	{
		public string Message;

		public ErrorEventArgs(string err)
		{
			this.Message = err;
		}
	}

	public class MessageEventArgs : EventArgs
	{
		public string Data = null;
		public byte[] RawData = null;
		public Opcode Type
		{
			get
			{
				return ( RawData == null ) ? Opcode.TEXT : Opcode.BINARY;
			}
		}
		
		public MessageEventArgs(string data)
		{
			this.Data = data;
		}

		public MessageEventArgs(byte[] data)
		{
			this.RawData = data;
		}
		
	}


	public class WebSocket
	{
		MessageWebSocket	socket;
		Uri					url;
		DataWriter			MessageWriter;
		System.Threading.Mutex	SendLock;
		
		//	one shared data writer;	http://stackoverflow.com/a/39653730

		public WebSocket(string Url)
		{
			url = TryGetUri( Url );
			socket = new MessageWebSocket();
			socket.MessageReceived += OnMessageRecieved;
			socket.Closed += OnClosed;
			MessageWriter = new DataWriter( socket.OutputStream );
			SendLock = new System.Threading.Mutex();
		}
	
		public event EventHandler<CloseEventArgs> OnClose;
		public event EventHandler<ErrorEventArgs> OnError;
		public event EventHandler<MessageEventArgs> OnMessage;
		public event EventHandler OnOpen;

		public bool IsAlive
		{
			get
			{
				return true;
			}
		}
        void JustCallOnClose(GameObject sender,CloseEventArgs e)
        {
			OnClose.Invoke(sender,e);
        }

		void OnMessageRecieved(MessageWebSocket FromSocket, MessageWebSocketMessageReceivedEventArgs InputMessage)
		{
			MessageEventArgs OutputMessage = null;

			if (InputMessage.MessageType == SocketMessageType.Utf8)
			{
				var stringLength = InputMessage.GetDataReader().UnconsumedBufferLength;
                string receivedMessage = InputMessage.GetDataReader().ReadString(stringLength);
				OutputMessage = new MessageEventArgs( receivedMessage );
			}
			else
			{
				//	todo!
				//OutputMessage = new MessageEventArgs( InputMessage.GetDataStream().ReadAsync );
			}
			
			OnMessage.Invoke( this, OutputMessage );
		}



	
		async Task	SendAsyncTask(string message)
		{
			//	"flush before changing type"
			await MessageWriter.FlushAsync();
			socket.Control.MessageType = SocketMessageType.Utf8;
			MessageWriter.WriteString(message);
			await MessageWriter.StoreAsync();
		}

		async Task	SendAsyncTask(byte[] Data)
		{
			//	"flush before changing type"
			await MessageWriter.FlushAsync();
			socket.Control.MessageType = SocketMessageType.Binary;
			MessageWriter.WriteBytes(Data);
			await MessageWriter.StoreAsync();
		}

		public void Send(string data)
		{
			lock(SendLock)
			{
				var SendTask = SendAsyncTask( data );
				SendTask.Wait();
			};
		}

		public void Send(byte[] data)
		{
			lock(SendLock)
			{
				var SendTask = SendAsyncTask( data );
				SendTask.Wait();
			};
		}



		public void SendAsync(string data, System.Action<bool> completed)
		{
			var ThreadPromise = ThreadPool.RunAsync( (Handler)=>
			{
				lock(SendLock)
				{
					var SendTask = SendAsyncTask( data );
					SendTask.Wait();
					completed.Invoke(true);
				};
				//await Send_Async(data);
			} );
			//	todo: launch a task, wait and then send completed
			//completed.Invoke(true);
		}

		public void SendAsync(byte[] data, System.Action<bool> completed)
		{
			var ThreadPromise = ThreadPool.RunAsync((Handler) =>
		   {
			   lock (SendLock)
			   {
				   var SendTask = SendAsyncTask(data);
				   SendTask.Wait();
				   completed.Invoke(true);
			   };
			   //await Send_Async(data);
		   });
			
			
			//completed.Invoke(true);
		}
		
		
		public void ConnectAsync ()
		{
			var ThreadPromise = StartAsync();
		}

		public void Close()
		{
			if (socket != null)
			{
				try
				{
					socket.Close(1000, "Closed due to user request.");
				}
				catch (Exception ex)
				{
					OnError.Invoke( this, new ErrorEventArgs(ex.Message) );
				}
				socket = null;
			}
		}

		void OnClosed(IWebSocket Socket,WebSocketClosedEventArgs Event)
		{
		}

		void OnRecvData(byte[] Data)
		{

		}
	
		static System.Uri TryGetUri(string uriString)
		{
			Uri webSocketUri;
			if (!Uri.TryCreate(uriString.Trim(), UriKind.Absolute, out webSocketUri))
				throw new System.Exception("Error: Invalid URI");
		
			// Fragments are not allowed in WebSocket URIs.
			if (!String.IsNullOrEmpty(webSocketUri.Fragment))
        		throw new System.Exception("Error: URI fragments not supported in WebSocket URIs.");

			// Uri.SchemeName returns the canonicalized scheme name so we can use case-sensitive, ordinal string
			// comparison.
			if ((webSocketUri.Scheme != "ws") && (webSocketUri.Scheme != "wss"))
        		throw new System.Exception("Error: WebSockets only support ws:// and wss:// schemes.");

			return webSocketUri;
		}

		async Task StartAsync()
		{
			/*	
			// If we are connecting to wss:// endpoint, by default, the OS performs validation of
			// the server certificate based on well-known trusted CAs. We can perform additional custom
			// validation if needed.
			if (SecureWebSocketCheckBox.IsChecked == true)
			{
				// WARNING: Only test applications should ignore SSL errors.
				// In real applications, ignoring server certificate errors can lead to Man-In-The-Middle
				// attacks. (Although the connection is secure, the server is not authenticated.)
				// Note that not all certificate validation errors can be ignored.
				// In this case, we are ignoring these errors since the certificate assigned to the localhost
				// URI is self-signed and has subject name = fabrikam.com
				streamWebSocket.Control.IgnorableServerCertificateErrors.Add(ChainValidationResult.Untrusted);
				streamWebSocket.Control.IgnorableServerCertificateErrors.Add(ChainValidationResult.InvalidName);
				// Add event handler to listen to the ServerCustomValidationRequested event. This enables performing
				// custom validation of the server certificate. The event handler must implement the desired
				// custom certificate validation logic.
				streamWebSocket.ServerCustomValidationRequested += OnServerCustomValidationRequested;
				// Certificate validation is meaningful only for secure connections.
				if (server.Scheme != "wss")
				{
					AppendOutputLine("Note: Certificate validation is performed only for the wss: scheme.");
				}
			}
			*/

			try
			{
				await socket.ConnectAsync(url);
			}
			catch (Exception ex) // For debugging
			{
				socket.Dispose();
				socket = null;

				//	gr: do error!
				OnError.Invoke( this, new ErrorEventArgs(ex.Message) );
				return;
			}
			OnOpen.Invoke( this, null );
		}
			/*
		// Continuously read incoming data. For reading data we'll show how to use activeSocket.InputStream.AsStream()
		// to get a .NET stream. Alternatively you could call readBuffer.AsBuffer() to use IBuffer with
		// activeSocket.InputStream.ReadAsync.
		private async Task ReceiveDataAsync(StreamWebSocket activeSocket)
		{
			Stream readStream = socket.InputStream.AsStreamForRead();
			try
			{
				byte[] readBuffer = new byte[1000];
				while (true)
				{
					if (socket != activeSocket)
					{
						// Our socket is no longer active. Stop reading.
						return;
					}
					//	gr: work out where messages split!
					int BytesRead = await readStream.ReadAsync(readBuffer, 0, readBuffer.Length);
				
					// Do something with the data.
					// This sample merely reports that the data was received.
					var PartBuffer = new byte[BytesRead];
					Array.Copy(readBuffer, PartBuffer, BytesRead);
					OnRecvData( PartBuffer );
				}
			}
			catch (Exception ex)
			{
				WebErrorStatus status = WebSocketError.GetStatus(ex.GetBaseException().HResult);
				switch (status)
				{
					case WebErrorStatus.OperationCanceled:
						OnError.Invoke( this, new ErrorEventArgs("Background read canceled.") );
						break;
					default:
						OnError.Invoke( this, new ErrorEventArgs("Read error: " + status + "; " + ex.Message) );
						break;
				}
			}
		}
		private async Task SendDataAsync(StreamWebSocket activeSocket)
		{
			try
			{
				// Send until the socket gets closed/stopped
				while (true)
				{
					if (socket != activeSocket)
					{
						// Our socket is no longer active. Stop sending.
						return;
					}
					if ( SendQueue == null || SendQueue.Count == 0 )
					{
						//	sleep thread plx
						await Task.Delay( TimeSpan.FromMilliseconds(500) );
						continue;
					}
					byte[] SendBuffer = null;
					lock(SendQueue)
					{
						SendBuffer = SendQueue[0];
						SendQueue.RemoveAt(0);
					}
					await activeSocket.OutputStream.WriteAsync( SendBuffer.AsBuffer() );
				}
			}
			catch (Exception ex)
			{
				WebErrorStatus status = WebSocketError.GetStatus(ex.GetBaseException().HResult);
				switch (status)
				{
					case WebErrorStatus.OperationCanceled:
						OnError.Invoke( this, new ErrorEventArgs("Background write canceled.") );
						break;
					default:
						OnError.Invoke( this, new ErrorEventArgs("Error " + status + " exception: " + ex.Message ) );
						break;
				}
			}
		}
		*/
	}
};



#endif