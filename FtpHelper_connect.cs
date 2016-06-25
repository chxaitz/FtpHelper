using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SysMisc
{
     partial class FtpHelper
    {
        #region 存贮FTP的连接结构类
        public class FTPConnect
        {
            #region 私有字段
            /// <summary>
            /// 数据传送套接字列表
            /// </summary>
            private List<Socket> m_DataSocketList;
            private string m_ID;
            /// <summary>
            /// 唯一ID
            /// </summary>
            public string ID
            {
                get { return this.m_ID; }
            }
            private object m_Tag = null;
            /// <summary>
            /// 扩展标记
            /// </summary>
            public object Tag
            {
                get { return this.m_Tag; }
                set { this.m_Tag = value; }
            }
            private bool m_DataTransmitting = false;
            /// <summary>
            /// 数据正在传输 标记
            /// </summary>
            public bool DataTransmitting
            {
                get { return this.m_DataTransmitting; }
                set { this.m_DataTransmitting = value; }
            }
            private Socket m_SocketControl;
            /// <summary>
            /// FTPUrl
            /// </summary>
            private FTPUrl _FTPUrl;
            /// <summary>
            /// 是否已经连接
            /// </summary>
            private bool m_IsConnected;
            private Encoding m_EncodeType = Encoding.Default;
            /// <summary>
            /// 编码方式
            /// </summary>
            public Encoding EncodeType
            {
                get { return this.m_EncodeType; }
                set { this.m_EncodeType = value; }
            }
            /// <summary>
            /// 接收和发送数据的缓冲区
            /// </summary>
            private static int BLOCK_SIZE = 512;
            /// <summary>
            /// 缓冲区大小
            /// </summary>
            private Byte[] m_Buffer;
            public Byte[] Buffer
            {
                get { return this.m_Buffer; }
                set { this.m_Buffer = value; }
            }
            private string m_Message;
            /// <summary>
            /// 当前的消息
            /// </summary>
            public string Message
            {
                get { return this.m_Message; }
                set { this.m_Message = value; }
            }
            private string m_ReplyString;
            /// <summary>
            /// 应答字符串
            /// </summary>
            public string ReplyString
            {
                get { return this.m_ReplyString; }
                set { this.m_ReplyString = value; }
            }
            private int m_ReplyCode;
            /// <summary>
            /// 应答代码
            /// </summary>
            public int ReplyCode
            {
                get { return this.m_ReplyCode; }
                set { this.m_ReplyCode = value; }
            }
            /// <summary>
            /// 传输模式
            /// </summary>
            private TransferType trType;
            #endregion
            public FTPConnect()
            {
                this.m_ID = System.Guid.NewGuid().ToString();
                this.m_DataSocketList = new List<Socket>();
                this.m_Buffer = new Byte[BLOCK_SIZE];
                this.FtpUrl = new FTPUrl();
            }
            public FTPConnect(FTPUrl ftpUrl)
            {
                this.m_ID = System.Guid.NewGuid().ToString();
                this.m_DataSocketList = new List<Socket>();
                this.m_Buffer = new Byte[BLOCK_SIZE];
                this.FtpUrl = ftpUrl.Clone() as FTPUrl;
            }
            public FTPConnect(FTPUrl ftpUrl, string ftpId)
            {
                if (String.IsNullOrEmpty(ftpId))
                    ftpId = System.Guid.NewGuid().ToString();
                this.m_ID = ftpId;
                this.m_DataSocketList = new List<Socket>();
                this.m_Buffer = new Byte[BLOCK_SIZE];
                this.FtpUrl = ftpUrl.Clone() as FTPUrl;
            }
            #region 公共字段
            /// <summary>
            /// 套接字连接
            /// </summary>
            public Socket SocketControl
            {
                get { return this.m_SocketControl; }
                set { this.m_SocketControl = value; }
            }
            /// <summary>
            /// 对应的URL，FtpConnect中的FtpUrl只维护IP，端口，用户名，密码，等连接信息
            /// </summary>
            public FTPUrl FtpUrl
            {
                get { return this._FTPUrl; }
                set { this._FTPUrl = value; }
            }
            /// <summary>
            /// 是否已经连接
            /// </summary>
            public bool IsConnected
            {
                get { return this.m_IsConnected; }
                set { this.m_IsConnected = value; }
            }
            #endregion
            #region 公共方法
            #region 取消传送数据
            public void CancelDataTransmit()
            {
                this.m_DataTransmitting = false;
            }
            #endregion
            #region 发送命令
            /// <summary>
            /// 发送命令并获取应答码和最后一行应答字符串
            /// </summary>
            /// <param name="strCommand">命令</param>
            public void SendCommand(string strCommand)
            {
                if (this.m_SocketControl == null)
                    throw (new Exception("请先连接服务器再进行操作！"));
                Byte[] cmdBytes = m_EncodeType.GetBytes((strCommand + "\r\n").ToCharArray());

                /*这段代码用来清空所有接收*/
                //int tm = this.m_SocketControl.ReceiveTimeout;
                //this.m_SocketControl.ReceiveTimeout = 10;
                //while (true)
                //{
                //    try {
                //        int iBytes = this.m_SocketControl.Receive(m_Buffer, m_Buffer.Length, 0);
                //    }catch(SocketException)
                //    {
                //        break;
                //    }
                //}
                //this.m_SocketControl.ReceiveTimeout = tm;
                /*这段代码用来清空所有接收*/
                if (!strCommand.Equals(""))
                {
                    int n = 0;
                    this.m_SocketControl.Send(cmdBytes, cmdBytes.Length, 0);
                    this.ReadReply();
                    //while (true)
                    //{
                    //    try
                    //    {
                    //        this.ReadReply();
                    //        if ((!this.ReplyCode.Equals("226")) || (n > 0))
                    //        {
                    //            break;
                    //        }
                    //    }
                    //    catch (SocketException) { break; }
                    //}
                }
                else
                {/*命令为空，则超时接收，保证接收缓冲区为空，保证下次接收，一般最为LIST命令后，接收226代码使用*/
                    int tm = this.m_SocketControl.ReceiveTimeout;
                    this.m_SocketControl.ReceiveTimeout = 10;
                    while (true)
                    {
                        try
                        {
                            int iBytes = this.m_SocketControl.Receive(m_Buffer, m_Buffer.Length, 0);
                            string msg = m_EncodeType.GetString(m_Buffer, 0, iBytes);
                            int reply_code = Int32.Parse(msg.Substring(0, 3));
                            
                            if (226 == reply_code)
                                break;
                        }
                        catch (SocketException)
                        {
                            break;
                        }
                    }
                    this.m_SocketControl.ReceiveTimeout = tm;
                }
            }
            #endregion
            #region 读取最后一行的消息
            /// <summary>
            /// 读取Socket返回的所有字符串
            /// </summary>
            /// <returns>包含应答码的字符串行</returns>
            private string ReadLine()
            {
                if (this.m_SocketControl == null)
                    throw (new Exception("请先连接服务器再进行操作！"));
                while (true)
                {
                    int iBytes = this.m_SocketControl.Receive(m_Buffer, m_Buffer.Length, 0);
                    m_Message += m_EncodeType.GetString(m_Buffer, 0, iBytes);
                    if (iBytes < m_Buffer.Length)
                    {
                        break;
                    }
                }
                char[] seperator = { '\n' };
                string[] mess = m_Message.Split(seperator);
                if (m_Message.Length > 2)
                {
                    m_Message = mess[mess.Length - 2];
                    //seperator[0]是10,换行符是由13和0组成的,分隔后10后面虽没有字符串,
                    //但也会分配为空字符串给后面(也是最后一个)字符串数组,
                    //所以最后一个mess是没用的空字符串
                    //但为什么不直接取mess[0],因为只有最后一行字符串应答码与信息之间有空格
                }
                else
                {
                    m_Message = mess[0];
                }
                if (!m_Message.Substring(3, 1).Equals(" "))//返回字符串正确的是以应答码(如220开头,后面接一空格,再接问候字符串)
                {
                    return this.ReadLine();
                }
                return m_Message;
            }
            #endregion
            #region 读取应答代码
            /// <summary>
            /// 将一行应答字符串记录在strReply和strMsg
            /// 应答码记录在iReplyCode
            /// </summary>
            public void ReadReply()
            {
                this.m_Message = "";
                this.m_ReplyString = this.ReadLine();
                this.m_ReplyCode = Int32.Parse(m_ReplyString.Substring(0, 3));
            }
            #endregion
            #region 断开连接
            /// <summary>
            /// 关闭连接
            /// </summary>
            public void DisConnect()
            {
                this.m_DataTransmitting = false;
                while (this.m_DataSocketList.Count > 0)
                {
                    Socket socket = this.m_DataSocketList[0];
                    if (socket != null && socket.Connected)
                        socket.Close();
                    this.m_DataSocketList.RemoveAt(0);
                }
                if (this.m_IsConnected && this.m_SocketControl != null)
                    this.SendCommand("QUIT");
                this.CloseSocketConnect();
                this.m_Buffer = null;
            }
            /// <summary>
            /// 关闭socket连接(用于登录以前)
            /// </summary>
            private void CloseSocketConnect()
            {
                if (this.m_SocketControl != null && this.m_SocketControl.Connected)
                {
                    this.m_SocketControl.Close();
                    this.m_SocketControl = null;
                }
                this.m_IsConnected = false;
            }
            #endregion
            #region 连接服务器
            public void Connect()
            {
                if (m_IsConnected == false)
                {
                    this.DisConnect();//先断开现有连接
                    this.m_Buffer = new byte[BLOCK_SIZE];
                    this.m_SocketControl = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                    IPEndPoint ep = new IPEndPoint(this._FTPUrl.RemoteHostIP, this._FTPUrl.RemotePort);
                    try
                    {
                        this.m_SocketControl.Connect(ep);
                        this.m_SocketControl.ReceiveTimeout = 500;
                    }
                    catch (Exception)
                    {
                        throw new IOException(String.Format("无法连接到远程服务器{0}！", this._FTPUrl.RemoteHost));
                    }
                    // 获取应答码
                    this.ReadReply();
                    if (m_ReplyCode != 220)
                    {
                        this.DisConnect();
                        throw new IOException(m_ReplyString.Substring(4));
                    }
                    // 登陆
                    this.SendCommand("USER " + this._FTPUrl.UserName);
                    if (!(m_ReplyCode == 331 || m_ReplyCode == 230))
                    {
                        this.CloseSocketConnect();//关闭连接
                        throw new IOException(m_ReplyString.Substring(4));
                    }
                    if (m_ReplyCode != 230)
                    {
                        this.SendCommand("PASS " + this._FTPUrl.Password);
                        if (!(m_ReplyCode == 230 || m_ReplyCode == 202))
                        {
                            this.CloseSocketConnect();//关闭连接
                            throw new IOException(m_ReplyString.Substring(4));
                        }
                    }
                    this.m_IsConnected = true;
                }
            }
            #endregion
            #region 改变目录
            /// <summary>
            /// 改变目录
            /// </summary>
            /// <param name="strDirName">新的工作目录名</param>
            public void ChangeFolder(string folderName)
            {
                if (!this.m_IsConnected)
                    throw (new Exception("请先连接服务器然后再进行CWD操作！"));
                if (folderName.Equals(".") || folderName.Equals(""))
                    return;
                if (folderName.Equals(".."))
                {
                    this.SendCommand("CDUP");/*返回上一层*/
                    string[] dirs = this._FTPUrl.Path.Split(new char[] { '/' });
                    this._FTPUrl.Path = String.Join("/", dirs, 0, dirs.Length - 1);
                }
                else
                {
                    this.SendCommand("CWD " + folderName);/*进入下一层*/
                    if (folderName.Equals("/"))
                        this._FTPUrl.Path = "/";
                    else
                    {/*正常的文件夹*/
                        if (this._FTPUrl.Path.Equals("/"))
                            this._FTPUrl.Path += folderName;
                        else
                            this._FTPUrl.Path += "/" + folderName;
                    }
                }
                //if (m_ReplyCode != 250)
                if ((m_ReplyCode != 250)&&(m_ReplyCode != 226))
                    throw new IOException(m_ReplyString.Substring(4));
            }
            /// <summary>
            /// 切换目录，相比ChangeFolder(string)优点就是，它可以一次切换多层目录。
            /// </summary>
            /// <param name="dirName"></param>
            public void TransformDir( string dirName)
            {
                dirName = dirName.Trim();
                if (!this.m_IsConnected)
                    throw (new Exception("请先连接服务器然后再进行CWD操作！"));
                if (dirName.Equals(".") || dirName.Equals(""))
                    return;
                if (dirName.Equals(_FTPUrl.Path) == true)
                    return;/*当前目录与操作目录相同，直接返回*/
                string[] dirs = dirName.Split(new char[] { '/' });
                if (dirs[0] == "")/*表示以“/xx”开头*/
                {
                    dirs[0] = "/";
                    this._FTPUrl.Path = "";
                }
                foreach( string dir in dirs)
                {
                    if (!dir.Equals(""))
                    {
                        ChangeFolder(dir);
                        if (this._FTPUrl.Path.Equals(""))
                            this._FTPUrl.Path += dir;
                        else if (this._FTPUrl.Path.Equals("/"))
                            this._FTPUrl.Path += dir;
                        else
                            this._FTPUrl.Path += "/" + dir;
                    }
                }
            }
            #endregion
            #region 传输模式
            /// <summary>
            /// 设置传输模式
            /// </summary>
            /// <param name="ttType">传输模式</param>
            public void SetTransferType(TransferType ttType)
            {
                if (ttType == TransferType.Binary)
                {
                    this.SendCommand("TYPE I");//binary类型传输
                }
                else
                {
                    this.SendCommand("TYPE A");//ASCII类型传输
                }
                if (m_ReplyCode != 200)
                {
                    throw new IOException(m_ReplyString.Substring(4));
                }
                else
                {
                    trType = ttType;
                }
            }
            /// <summary>
            /// 获得传输模式
            /// </summary>
            /// <returns>传输模式</returns>
            public TransferType GetTransferType()
            {
                return trType;
            }
            #endregion
            #region 建立进行数据连接的socket
            /// <summary>
            /// 建立进行数据连接的socket
            /// </summary>
            /// <returns>数据连接socket</returns>
            public Socket CreateDataSocket()
            {
                this.SendCommand("PASV");
                if (this.m_ReplyCode != 227)
                    throw new IOException(this.m_ReplyString.Substring(4));
                int index1 = this.m_ReplyString.IndexOf('(');
                int index2 = this.m_ReplyString.IndexOf(')');
                string ipData = this.m_ReplyString.Substring(index1 + 1, index2 - index1 - 1);
                int[] parts = new int[6];
                int len = ipData.Length;
                int partCount = 0;
                string buf = "";
                for (int i = 0; i < len && partCount <= 6; i++)
                {
                    char ch = Char.Parse(ipData.Substring(i, 1));
                    if (Char.IsDigit(ch))
                        buf += ch;
                    else if (ch != ',')
                    {
                        throw new IOException("Malformed PASV Reply: " + this.m_ReplyString);
                    }
                    if (ch == ',' || i + 1 == len)
                    {
                        try
                        {
                            parts[partCount++] = Int32.Parse(buf);
                            buf = "";
                        }
                        catch (Exception)
                        {
                            throw new IOException("Malformed PASV Reply: " + this.m_ReplyString);
                        }
                    }
                }
                string ipAddress = parts[0] + "." + parts[1] + "." + parts[2] + "." + parts[3];
                int port = (parts[4] << 8) + parts[5];
                Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                IPEndPoint ep = new IPEndPoint(IPAddress.Parse(ipAddress), port);
                try
                {
                    socket.Connect(ep);
                }
                catch (Exception)
                {
                    throw new IOException(String.Format("无法连接到远程服务器{0}:{1}！", ipAddress, port));
                }
                this.m_DataSocketList.Add(socket);
                return socket;
            }
            #endregion
            #endregion
        }
        #endregion


        #region FTPUrl结构
        public class FTPUrl:ICloneable
        {
            private string m_Path = String.Empty;
            private string m_RemoteHost = String.Empty;
            private IPAddress m_RemoteHostIP = IPAddress.None;
            private int m_RemotePort = 21;
            private string m_UserName = String.Empty;
            private string m_UserPwd = String.Empty;
            private string m_SubPath = String.Empty;
            public FTPUrl()
            { }
            public FTPUrl(Uri uri, string userName, string userPassword)
            {
                this.Uri = uri;
                try {
                    this.m_RemoteHostIP = IPAddress.Parse(uri.Host);
                }
                catch (Exception ex) { LogHelper.Error(typeof(FTPUrl), ex); m_RemoteHostIP = IPAddress.Parse("127.0.0.1"); }
                this.m_RemotePort = uri.Port;
                this.m_UserName = userName;
                this.m_UserPwd = userPassword;
                this.m_Path = uri.AbsolutePath; ;
            }
            /// <summary>
            /// FTP的全地址
            /// </summary>
            public string Path
            {
                get { return this.m_Path; }
                set { this.m_Path = value; }
            }
            /// <summary>
            /// 主机地址
            /// </summary>
            public string RemoteHost
            {
                get { return this.m_RemoteHost; }
            }
            /// <summary>
            /// 主机IP
            /// </summary>
            public IPAddress RemoteHostIP
            {
                get { return this.m_RemoteHostIP; }
                set { this.m_RemoteHostIP = value; }
            }
            public Uri Uri { get; set; }
            /// <summary>
            /// 主机端口
            /// </summary>
            public int RemotePort
            {
                get { return this.m_RemotePort; }
                set { this.m_RemotePort = value; }
            }
            public string UserName
            {
                get { return this.m_UserName; }
                set { this.UserName = value; }
            }
            public string Password
            {
                get { return this.m_UserPwd; }
                set { this.Password = value; }
            }
            public string SubPath
            {
                get { return this.m_SubPath; }
                set { this.m_SubPath = value; }
            }
            public object Clone()
            {
                FTPUrl url = new FTPUrl();
                url.m_RemoteHostIP = this.m_RemoteHostIP;
                url.m_RemoteHost = this.m_RemoteHost;
                url.m_RemotePort = this.m_RemotePort;
                url.m_UserName = this.m_UserName;
                url.m_UserPwd = this.m_UserPwd;
                url.m_Path = "/";
                return url;
            }
        }
        #endregion

        #region 传输模式
        /// <summary>
        /// 传输模式:二进制类型、ASCII类型
        /// </summary>
        public enum TransferType
        {
            /// <summary>
            /// Binary
            /// </summary>
            Binary,
            /// <summary>
            /// ASCII
            /// </summary>
            ASCII
        };
        #endregion

        #region 文件信息结构
        public enum FileListStyle
        {
            UnixStyle,
            WindowsStyle,
            Unknown
        }
        #endregion

        #region 文件信息结构
        public class FtpFileInfo
        {
            public string Flags;
            public string Owner;
            public string Group;
            public long FileSize;
            public bool IsFolder;
            public DateTime CreateTime;
            /// <summary>
            /// 获取文件名
            /// </summary>
            public string Name
            {
                get { return getName(); }
                set { setName(value); }
            }
            /// <summary>
            /// 获取目录或文件的完整路径
            /// </summary>
            public string FullName { get { return _fullName; } set { _fullName = value.Trim(); } }
            /// <summary>
            /// 获取表示目录的完整路径的字符串
            /// </summary>
            public string DirectoryName {
                get { return getDirectoryName(); }
            }
            public string Guid;/*自己添加的，用于下载时候的唯一识别字符串*/

            public FtpFileInfo()
            {
            }
            public FtpFileInfo( string ftpFullName)
            {
                _fullName = ftpFullName.Trim();
            }

            /*私有变量*/
            private string _fullName = "";
            /*私有方法*/
            private string getName()
            {
                if (_fullName.IndexOf('/') >= 0)
                {
                    string[] nmes = _fullName.Split(new char[] { '/' });
                    return nmes[nmes.Length-1];/*返回最后一个字符串，表示文件名*/
                }
                else {/*路径中不包含'/'符号，则表示只含文件名*/
                    return _fullName;
                }
            }

            private void setName( string ftpName)
            {
                if ((_fullName.Equals("")) || (_fullName.Equals("/")))
                    _fullName += ftpName;
                else
                    _fullName += "/" + ftpName;
            }

            private string getDirectoryName()
            {
                if (_fullName.IndexOf('/') >= 0)
                {/*路径中即使包含'/'符号，也不能保证最后一项是文件，但假设最后一个是文件，而取得从第一个到倒数第二个，且最后一个目录不包含"/"符号*/
                    string[] dirs = _fullName.Split(new char[] { '/' });
                    string dic_name="";
                    dic_name = String.Join("/", dirs, 0, dirs.Length - 1);
                    return dic_name;
                }
                else
                {/*路径中不包含'/'符号，则假设，此就是目录，因为无法知道此是否是目录*/
                    return _fullName;
                }
            }

            private void setDirectoryName( string ftpDirectory)
            {

            }

            public override string ToString()
            {
                return getName();
            }
        }
        #endregion
    }
}
