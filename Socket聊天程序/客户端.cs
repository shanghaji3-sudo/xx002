using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace Socket聊天程序
{
    public partial class 客户端 : Form
    {
        private readonly List<Socket> clientSocketList = new List<Socket>();
        private Socket serverSocket;
        private const int MaxMsgLen = 1024 * 1024;

        public 客户端()
        {
            InitializeComponent();

            txtMessage.Enabled = false;
        }

        private void btnStart_Click(object sender, EventArgs e)
        {
            IPAddress ip;
            if (string.IsNullOrWhiteSpace(txtIp.Text) || txtIp.Text.Trim() == "0.0.0.0")
            {
                ip = IPAddress.Any;
            }
            else if (!IPAddress.TryParse(txtIp.Text.Trim(), out ip))
            {
                MessageBox.Show("IP 地址格式不正确，请检查后重试。");
                return;
            }

            int port;
            if (!int.TryParse(txtPort.Text, out port) || port < 1 || port > 65535)
            {
                MessageBox.Show("端口号必须是 1-65535 之间的整数，请检查后重试。");
                return;
            }

            try
            {
                Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                socket.Bind(new IPEndPoint(ip, port));
                socket.Listen(10);
                serverSocket = socket;
                ThreadPool.QueueUserWorkItem(AcceptClientConnect, socket);
                btnStart.Enabled = false;
                btnStop.Enabled = true;
            }
            catch (SocketException ex)
            {
                MessageBox.Show("启动失败：" + ex.Message);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString());
            }
        }

        private void btnStop_Click(object sender, EventArgs e)
        {
            StopServer();
            btnStart.Enabled = true;
            btnStop.Enabled = false;
            AppendTextToTxtLog("服务器已停止监听");
        }

        private void StopServer()
        {
            if (serverSocket != null)
            {
                try { serverSocket.Close(); }
                catch { }
                serverSocket = null;
            }

            List<Socket> snapshot;
            lock (clientSocketList)
            {
                snapshot = clientSocketList.ToList();
                clientSocketList.Clear();
            }
            foreach (var s in snapshot)
            {
                try { s.Close(); }
                catch { }
            }
        }

        private void AcceptClientConnect(object socket)
        {
            var acceptSocket = socket as Socket;

            AppendTextToTxtLog("服务器端开始接受客户端链接");
            while (true)
            {
                Socket proxSocket = null;
                try
                {
                    proxSocket = acceptSocket.Accept();
                }
                catch (Exception ex)
                {
                    AppendTextToTxtLog("监听异常，停止接受客户端链接：" + ex.Message);
                    return;
                }

                AppendTextToTxtLog(string.Format("客户端:{0}链接上了", GetRemotePoint(proxSocket)));

                lock (clientSocketList)
                {
                    clientSocketList.Add(proxSocket);
                }
                ThreadPool.QueueUserWorkItem(ReceiveData, proxSocket);
            }
        }

        private void ReceiveData(object socket)
        {
            var proxSocket = socket as Socket;
            byte[] temp = new byte[MaxMsgLen];
            List<byte> buffer = new List<byte>();

            while (true)
            {
                int len = 0;
                try
                {
                    len = proxSocket.Receive(temp, 0, temp.Length, SocketFlags.None);
                }
                catch
                {
                    CloseAndRemoveClient(proxSocket,
                        string.Format("客户端:{0}异常退出", GetRemotePoint(proxSocket)));
                    return;
                }

                if (len <= 0)
                {
                    CloseAndRemoveClient(proxSocket,
                        string.Format("客户端:{0}正常退出", GetRemotePoint(proxSocket)));
                    return;
                }

                byte[] chunk = new byte[len];
                Buffer.BlockCopy(temp, 0, chunk, 0, len);
                buffer.AddRange(chunk);

                int idx;
                while ((idx = buffer.IndexOf((byte)'\n')) >= 0)
                {
                    int bodyLen = (idx > 0 && buffer[idx - 1] == (byte)'\r') ? idx - 1 : idx;
                    byte[] lineBytes = buffer.GetRange(0, bodyLen).ToArray();
                    buffer.RemoveRange(0, idx + 1);

                    string str = Encoding.UTF8.GetString(lineBytes);
                    AppendTextToTxtLog(string.Format("接收到客户端:{0}的消息是:{1}",
                        GetRemotePoint(proxSocket), str));
                }

                if (buffer.Count > MaxMsgLen)
                {
                    CloseAndRemoveClient(proxSocket,
                        string.Format("客户端:{0}消息超长且未以换行结尾，断开连接", GetRemotePoint(proxSocket)));
                    return;
                }
            }
        }

        private string GetRemotePoint(Socket s)
        {
            try
            {
                EndPoint ep = s.RemoteEndPoint;
                return ep == null ? "未知" : ep.ToString();
            }
            catch
            {
                return "未知";
            }
        }

        private void CloseAndRemoveClient(Socket proxSocket, string logMsg)
        {
            lock (clientSocketList)
            {
                clientSocketList.Remove(proxSocket);
            }
            try
            {
                proxSocket.Close();
            }
            catch
            {
            }
            AppendTextToTxtLog(logMsg);
        }

        private void AppendTextToTxtLog(string txt)
        {
            try
            {
                if (txtMessage.InvokeRequired)
                {
                    txtMessage.Invoke(new Action<string>(s =>
                    {
                        txtMessage.Text = string.Format("{0}\r\n{1}", s, txtMessage.Text);
                    }), txt);
                }
                else
                {
                    txtMessage.Text = string.Format("{0}\r\n{1}", txt, txtMessage.Text);
                }
            }
            catch
            {
            }
        }

        private void btnSend_Click(object sender, EventArgs e)
        {
            List<Socket> snapshot;
            lock (clientSocketList)
            {
                snapshot = clientSocketList.ToList();
            }

            byte[] data = Encoding.UTF8.GetBytes(txtSend.Text + "\r\n");

            foreach (var proxSocket in snapshot)
            {
                try
                {
                    proxSocket.Send(data, 0, data.Length, SocketFlags.None);
                }
                catch (Exception ex)
                {
                    CloseAndRemoveClient(proxSocket,
                        string.Format("向客户端:{0}发送失败，连接已移除:{1}",
                            GetRemotePoint(proxSocket), ex.Message));
                }
            }
        }

        private void MainFrm_FormClosing(object sender, FormClosingEventArgs e)
        {
            StopServer();
        }

        private void btnClear_Click(object sender, EventArgs e)
        {
            txtMessage.Text = "";
        }
    }
}
