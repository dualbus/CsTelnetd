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
    // This is the program's starting point (i.e. its "Main" class).
    public class Program
    {
        // Port we will be listening to.
        private static readonly int _port = 9999;
        // Shell to execute on each connection.
        private static readonly string _command = "cmd.exe";

        static void Main(string[] args)
        {
            // Create a new TCP/IPv4 socket, bind it to 0.0.0.0:_port, so we can listen for requests on all
            // network interfaces. Put the socket on listening mode, and accept connections for ever.
            Socket serverSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            serverSocket.Bind((EndPoint)new IPEndPoint(IPAddress.Any, _port));
            serverSocket.Listen(5);
            while (true)
            {
                // Each accepted connection will create a non-blocking thread, so the main thread can continue to listen on
                // the server socket for new connections. The child thread receives the new socket created by accept, and
                // the command to execute. We don't track these threads, so the server has no way to communicate with them or
                // know how many clients we're handling at the time.
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

    // This class handles an accepted connection.
    //
    // Its parameters are the socket created by accept, and a command to execute on the server machine.
    public class Connection
    {
        private Socket _socket;
        private ProcessStartInfo _processStartInfo;

        // The constructor.
        //
        // This method stores the accepted socket as a private instance field, and initializes a new
        // System.Diagnostics.ProcessStartInfo object that we can pass to System.Diagnostics.Process to start
        // a process on the server machine. All of the standard file descriptors of the process are redirected,
        // because we will connect them to the socket.
        public Connection(Socket socket, string command)
        {
            _socket = socket;
            _processStartInfo = new ProcessStartInfo(command);
            _processStartInfo.UseShellExecute = false;
            _processStartInfo.RedirectStandardInput = true;
            _processStartInfo.RedirectStandardOutput = true;
            _processStartInfo.RedirectStandardError = true;
        }

        // Method passed to ThreadStart.
        //
        // This is the thread's starting point. It starts the process on the server machine, creates three ``state objects'', which will
        // help us handle the redirections asynchronously. We associate the BeginRead methods of the standard output and error streams
        // with our call-back (LocalReadCallback), to start processing the output of the command. We also associate the BeginRead() method
        // the socket to our socket read call-back (ReadCallback), so we can process the data received through the socket.
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

        // Asynchronous read call-back for the socket object.
        //
        // This call-back method fills the state object's buffer with the received data, and writes it to the process' standard input.
        private void ReadCallback(IAsyncResult iAsyncResult)
        {
            StateObject stateObject = (StateObject)iAsyncResult.AsyncState;
            Socket socket = stateObject.Socket;
            NVT nvt = new NVT();
            try
            {
                int read = socket.EndReceive(iAsyncResult);
                if (0 < read)
                {
                    // Handle the received data and reassociate the call-back with the socket's BeginReceive().
                    StreamWriter streamWriter = stateObject.Process.StandardInput;
                    nvt.Write(stateObject.Buffer, 0, read);
                    int r = nvt.Read(stateObject.Buffer, 0, read);
                    streamWriter.Write(Encoding.ASCII.GetString(stateObject.Buffer, 0, r));
                    socket.BeginReceive(stateObject.Buffer, 0, StateObject.BUFFER_SIZE, 0, new AsyncCallback(ReadCallback), stateObject);
                }
                else
                {
                    socket.Close();
                }
            }
            catch(ObjectDisposedException)
            {
                // If we reach this point, it means that either the socket or the process' standard input were closed, so we have nothing
                // else to do here. Close the socket and the process, just in case.
                stateObject.Socket.Close();
                stateObject.Process.Close();
            }
        }

        // Asynchronous read call-back for the standard output and error streams of the executed process.
        //
        // This call-back method fills the state object's buffer with the received data, and writes it to the socket.
        private void LocalReadCallback(IAsyncResult iAsyncResult)
        {
            LocalStateObject localStateObject = (LocalStateObject)iAsyncResult.AsyncState;
            Socket socket = localStateObject.Socket;
            Stream stream = localStateObject.Stream;
            NVT nvt = new NVT();
            try
            {
                int read = stream.EndRead(iAsyncResult);
                if (0 < read)
                {
                    nvt.Write(localStateObject.Buffer, 0, read);
                    int r = nvt.Read(localStateObject.Buffer, 0, read);
                    // Lock the socket, just in case the other call-back wants to write at the same time.
                    lock (socket)
                    {
                        socket.Send(localStateObject.Buffer, r, 0);
                    }

                    // Reassociate the stream's BeginRead() method with the LocalReadCallback().
                    stream.BeginRead(localStateObject.Buffer, 0, LocalStateObject.BUFFER_SIZE, new AsyncCallback(LocalReadCallback), localStateObject);
                }
                else
                {
                    stream.Close();
                }
            }
            catch (ObjectDisposedException)
            {
                // If we reach this point, it means that either the socket or the processed stream were closed, so we have nothing
                // else to do here. Close the socket, the process, and the stream, just in case.
                localStateObject.Socket.Close();
                localStateObject.Process.Close();
                localStateObject.Stream.Close();
            }
        }

        // This is a helper class for the ReadCallback() method.
        //
        // We keep a reference to the socket, and the process, and a moderate sized buffer.
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

        // This is a helper class for the LocalReadCallback() method.
        //
        // We keep a reference to the socket, the process, the handled stream, and a moderate sized buffer.
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
