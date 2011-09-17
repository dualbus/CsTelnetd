using System;
using System.Collections.Generic;
using System.Text;
using System.Net;
using System.Net.Sockets;
using System.IO;
using System.Diagnostics;
using System.Threading;

namespace Telnetd
{
    public class Program
    {
        private static readonly int _port = 9999;
        private static readonly string _command = @"cmd.exe";

        static void Main(string[] args)
        {
            Socket serverSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            serverSocket.Bind((EndPoint)new IPEndPoint(IPAddress.Any, _port));
            serverSocket.Listen(5);
            while (true)
            {
                Socket clientConnection = serverSocket.Accept();
                System.Console.WriteLine(string.Format("{0} {1}", clientConnection.RemoteEndPoint, DateTime.Now));
                Thread thread = new Thread(new ThreadStart(() => 
                    { 
                        new Connection(clientConnection, _command).DoWork();
                    }));
                thread.Start();
            }
        }
    }

    public class Connection
    {
        private Socket _socket;
        private ProcessStartInfo _processStartInfo;

        public Connection(Socket socket, string command)
        {
            _socket = socket;
            _processStartInfo = new ProcessStartInfo(command);
            _processStartInfo.UseShellExecute = false;
            _processStartInfo.RedirectStandardInput = true;
            _processStartInfo.RedirectStandardOutput = true;
            _processStartInfo.RedirectStandardError = true;
        }

        public void DoWork()
        {
                Process process = Process.Start(_processStartInfo);
                LocalStateObject outputLocalStateObject = new LocalStateObject(_socket, process, process.StandardOutput.BaseStream);
                process.StandardOutput.BaseStream.BeginRead(outputLocalStateObject.Buffer, 0, LocalStateObject.BUFFER_SIZE, new AsyncCallback(LocalReadCallback), outputLocalStateObject);
                LocalStateObject errorLocalStateObject = new LocalStateObject(_socket, process, process.StandardError.BaseStream);
                process.StandardError.BaseStream.BeginRead(errorLocalStateObject.Buffer, 0, LocalStateObject.BUFFER_SIZE, new AsyncCallback(LocalReadCallback), errorLocalStateObject);
                StateObject stateObject = new StateObject(_socket, process);
                _socket.BeginReceive(stateObject.Buffer, 0, StateObject.BUFFER_SIZE, 0, new AsyncCallback(ReadCallback), stateObject);
                process.WaitForExit();
                _socket.Close();
        }

        private void ReadCallback(IAsyncResult iAsyncResult)
        {
            StateObject stateObject = (StateObject)iAsyncResult.AsyncState;
            Socket socket = stateObject.Socket;
            try
            {
                int read = socket.EndReceive(iAsyncResult);
                if (0 < read)
                {
                    StreamWriter streamWriter = stateObject.Process.StandardInput;
                    streamWriter.Write(Encoding.UTF8.GetString(stateObject.Buffer, 0, read));
                    socket.BeginReceive(stateObject.Buffer, 0, StateObject.BUFFER_SIZE, 0, new AsyncCallback(ReadCallback), stateObject);
                }
                else
                {
                    socket.Close();
                }
            }
            catch(ObjectDisposedException)
            {
            }
        }

        private void LocalReadCallback(IAsyncResult iAsyncResult)
        {
            LocalStateObject localStateObject = (LocalStateObject)iAsyncResult.AsyncState;
            Socket socket = localStateObject.Socket;
            Stream stream = localStateObject.Stream;
            int read = stream.EndRead(iAsyncResult);
            if (0 < read)
            {
                lock (socket)
                {
                    socket.Send(localStateObject.Buffer, read, 0);
                }
                stream.BeginRead(localStateObject.Buffer, 0, LocalStateObject.BUFFER_SIZE, new AsyncCallback(LocalReadCallback), localStateObject);
            }
            else
            {
                stream.Close();
            }
        }


        private class StateObject
        {
            public const int BUFFER_SIZE = 1024;
            protected byte[] _buffer;
            protected Socket _socket;
            protected Process _process;

            public StateObject(Socket socket, Process process)
            {
                _buffer = new byte[BUFFER_SIZE];
                _socket = socket;
                _process = process;
            }

            public byte[] Buffer
            {
                get
                {
                    return _buffer;
                }
            }

            public Socket Socket 
            {
                get
                {
                    return _socket;
                }
            }

            public Process Process {
                get
                {
                    return _process;
                }
            }
        }

        private class LocalStateObject : StateObject
        {
            protected Stream _stream;

            public LocalStateObject(Socket socket, Process process, Stream stream) : base(socket, process)
            {
                _stream = stream;
            }

            public Stream Stream
            {
                get
                {
                    return _stream;
                }
            }
        }
    }
}
