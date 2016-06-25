using System;
using System.Net;
using System.Net.Sockets;
using System.Collections.Generic;
using System.Text;
using System.IO;
using System.Globalization;
using System.Text.RegularExpressions;

namespace SysMisc
{
    /// <summary>
    /// 一个FtpHelper维持一个主FtpConnect连接，用来保证目录操作等，而如果需要进行异步操作，则会重新生成一个新的FtpConnect连接，
    /// 并将其加入FtpConnect_List链表，由FtpHelper进行维护。
    /// </summary>
    public partial class FtpHelper : IDisposable
    {
        #region 变量区
        /// <summary>
        /// 进行控制连接的socket
        /// </summary>
        private List<FTPConnect> m_FTPConnectList;
        private object m_Tag;
        /// <summary>
        /// 标记
        /// </summary>
        public object Tag
        {
            get { return this.m_Tag; }
            set { this.m_Tag = value; }
        }
        public FTPUrl FtpUrl{get{ return _FtpConnect.FtpUrl; }}
        #endregion
        
        private string _ErrorMsg;
        private WebProxy _Proxy = null;
        private bool _isDeleteTempFile = false;
        private string _UploadTempFile = "";
        private FTPConnect _FtpConnect = null;
        #region 实例化
        public FtpHelper()
        {
            this.m_FTPConnectList = new List<FTPConnect>();
        }
        /// <summary>
        /// 初始化FTP连接
        /// </summary>
        /// <param name="FtpUri">格式如“ftp://127.0.0.1:21/test”,从中解析出，地址，端口，和工作目录</param>
        /// <param name="strUserName"></param>
        /// <param name="strPassword"></param>
        public FtpHelper(Uri FtpUri, string strUserName, string strPassword)
        {
            this._Proxy = null;
            FTPUrl FtpUrl = new FTPUrl( FtpUri, strUserName, strPassword );
            this.m_FTPConnectList = new List<FTPConnect>();
            this._FtpConnect = new FTPConnect(FtpUrl, "");
        }
        #endregion
        #region Dispose
        public void Dispose()
        {
            while (this.m_FTPConnectList.Count > 0)
            {
                FTPConnect ftpConnect = this.m_FTPConnectList[0];
                ftpConnect.DisConnect();
                this.m_FTPConnectList.Remove(ftpConnect);
            }
        }
        #endregion
        public bool Connect()
        {
            bool Success = false;
            try
            {
                this._FtpConnect.Connect();
                if( this.m_FTPConnectList.IndexOf( this._FtpConnect )<0 )
                    this.m_FTPConnectList.Add(this._FtpConnect);
                Success = true;
            }
            catch (Exception ex) { LogHelper.Error(typeof(FtpHelper), ex); this._ErrorMsg = ex.ToString(); }
            return Success;
        }
        /// <summary>
        /// 得到文件大小
        /// </summary>
        /// <param name="remoteFileName">目标文件，包含用户名与密码。如：ftp://username:password@127.0.0.1/1.txt</param>
        /// <param name="UserName">用户名</param>
        /// <param name="Password">密码</param>
        /// <returns></returns>
        public long GetFileSize(string remoteFileName)
        {
            long lFileSize = 0;
            try
            {
                lFileSize = readFileSize( this._FtpConnect, new FtpFileInfo( this.FtpUrl.Path + "/" + remoteFileName));
            }catch(Exception ex) { LogHelper.Error(typeof(FtpHelper),ex); throw ex; }
            return lFileSize;
        }
        #region 删除指定的文件
        /// <summary>
        /// 删除指定的文件
        /// </summary>
        /// <param name="remoteFileName">待删除文件名</param>
        public void DeleteFile(string remoteFileName)
        {
            try
            {
                this._FtpConnect.SendCommand("DELE " + remoteFileName);
                if (this._FtpConnect.ReplyCode != 250)
                    throw (new Exception(this._FtpConnect.ReplyString.Substring(4)));
            }
            catch (Exception ex)
            {
                throw (ex);
            }
        }
        #endregion
        #region 重命名
        /// <summary>
        /// 重命名(如果新文件名与已有文件重名,将覆盖已有文件)
        /// </summary>
        /// <param name="strOldFileName">旧文件名</param>
        /// <param name="strNewFileName">新文件名</param>
        public void Rename(string remoteOrgName, string remoteNewName)
        {
            try
            {
                this._FtpConnect.SendCommand("RNFR " + remoteOrgName);
                if (this._FtpConnect.ReplyCode != 350)
                    throw (new Exception(this._FtpConnect.ReplyString.Substring(4)));
                this._FtpConnect.SendCommand("RNTO " + remoteNewName);
                if (this._FtpConnect.ReplyCode != 250)
                    throw (new Exception(this._FtpConnect.ReplyString.Substring(4)));
            }
            catch (Exception ex)
            {
                throw (ex);
            }
        }
        #endregion
        #region 创建文件夹
        public void MakeDirectory(string remoteFolderName)
        {
            try
            {
                this._FtpConnect.SendCommand("MKD " + remoteFolderName);
                if (this._FtpConnect.ReplyCode != 257 && this._FtpConnect.ReplyCode != 550)
                    throw new IOException(this._FtpConnect.ReplyString.Substring(4));
            }
            catch (Exception ex)
            {
                throw (ex);
            }
        }
        #endregion
        #region 删除目录
        /// <summary>
        /// 删除目录（包括目录内的所有文件），输入目录参数，不支持层级，比如"\folder1\folder2"是非法的。
        /// </summary>
        /// <param name="remoteFolderName">目录名</param>
        public void DeleteDirectory(string remoteFolderName)
        {
            try
            {
                this._FtpConnect.SendCommand("RMD " + remoteFolderName);
                if (this._FtpConnect.ReplyCode != 250)
                    throw (new Exception(this._FtpConnect.ReplyString.Substring(4)));
            }
            catch (Exception ex)
            {
                throw (ex);
            }
        }
        #endregion
        #region 得到详细的文件列表
        public List<FtpFileInfo> ListFilesAndFolders()
        {
            Socket socketData = null;
            try
            {
                socketData = this._FtpConnect.CreateDataSocket();
                this._FtpConnect.SendCommand("LIST");
                if (this._FtpConnect.ReplyCode != 150 && this._FtpConnect.ReplyCode != 125 && this._FtpConnect.ReplyCode != 226)
                    throw new IOException(this._FtpConnect.ReplyString.Substring(4));
                //获得结果
                this._FtpConnect.Message = "";
                while (true)
                {
                    int iBytes = socketData.Receive(this._FtpConnect.Buffer, this._FtpConnect.Buffer.Length,SocketFlags.None);
                    this._FtpConnect.Message += this._FtpConnect.EncodeType.GetString(this._FtpConnect.Buffer, 0, iBytes);
                    if (iBytes < this._FtpConnect.Buffer.Length)
                        break;
                }
                if (this._FtpConnect.Message.StartsWith("t"))
                    this._FtpConnect.Message = this._FtpConnect.Message.Substring(this._FtpConnect.Message.IndexOf("\r\n") + 2);
                return this.getList(this._FtpConnect.Message);
            }
            catch (Exception ex)
            {
                throw (ex);
            }
            finally
            {
                this._FtpConnect.SendCommand("");
                if (socketData != null && socketData.Connected)
                    socketData.Close();
                socketData = null;
            }
        }
        #region 用于得到文件列表的方法
        /// <summary>
        /// 列出FTP服务器上面当前目录的所有文件
        /// </summary>
        /// <param name="listUrl">查看目标目录</param>
        /// <returns>返回文件信息列表</returns>
        public List<FtpFileInfo> ListFiles()
        {
            List<FtpFileInfo> listFile = null;
            try
            {
                List<FtpFileInfo> listAll = this.ListFilesAndFolders();
                listFile = new List<FtpFileInfo>();
                foreach (FtpFileInfo file in listAll)
                {
                    if (!file.IsFolder)
                    {
                        listFile.Add(file);
                    }
                }
            }
            catch (Exception ex)
            {
                throw ex;
            }
            return listFile;
        }
        /// <summary>
        /// 列出FTP服务器上面当前目录的所有的目录
        /// </summary>
        /// <param name="listUrl">查看目标目录</param>
        /// <returns>返回目录信息列表</returns>
        public List<FtpFileInfo> ListFolders()
        {
            List<FtpFileInfo> listAll = this.ListFilesAndFolders();
            List<FtpFileInfo> listDirectory = new List<FtpFileInfo>();
            foreach (FtpFileInfo file in listAll)
            {
                if (file.IsFolder)
                {
                    listDirectory.Add(file);
                }
            }
            return listDirectory;
        }

        /// <summary>
        /// 获得文件和目录列表
        /// </summary>
        /// <param name="datastring">FTP返回的列表字符信息</param>
        private List<FtpFileInfo> getList(string datastring)
        {
            List<FtpFileInfo> myListArray = new List<FtpFileInfo>();
            string[] dataRecords = datastring.Split('\n');
            FileListStyle _directoryListStyle = guessFileListStyle(dataRecords);
            foreach (string s in dataRecords)
            {
                if (_directoryListStyle != FileListStyle.Unknown && s != "")
                {
                    FtpFileInfo f = new FtpFileInfo();
                    f.Name = "..";
                    switch (_directoryListStyle)
                    {
                        case FileListStyle.UnixStyle:
                            f = parseFileStructFromUnixStyleRecord(s);
                            break;
                        case FileListStyle.WindowsStyle:
                            f = parseFileStructFromWindowsStyleRecord(s);
                            break;
                    }
                    if (!(f.Name == "." || f.Name == ".."))
                    {
                        myListArray.Add(f);
                    }
                }
            }
            return myListArray;
        }

        #region 目录或文件存在的判断
        /// <summary>
        /// 判断当前目录下指定的子目录是否存在（仅支持当前远程目录下的目录查找）
        /// </summary>
        /// <param name="remoteFolderName">指定的目录名</param>
        public bool FolderExist(string remoteFolderName)
        {
            try
            {
                if (!isValidPathChars(remoteFolderName))
                {
                    throw new Exception("目录名非法！");
                }
                List<FtpFileInfo> listDir = ListFolders();
                foreach (FtpFileInfo dir in listDir)
                {
                    if (dir.Name == remoteFolderName)
                    {
                        return true;
                    }
                }
                return false;
            }
            catch (Exception ep)
            {
                this._ErrorMsg = ep.ToString();
                throw ep;
            }
        }
        /// <summary>
        /// 判断一个远程文件是否存在服务器当前目录下面（仅支持当前远程目录下的文件查找）
        /// </summary>
        /// <param name="remoteFileName">远程文件名</param>
        public bool FileExist(string remoteFileName)
        {
            try
            {
                if (!isValidFileChars(remoteFileName))
                {
                    throw new Exception("文件名非法！");
                }
                List<FtpFileInfo> listFile = ListFiles();
                foreach (FtpFileInfo file in listFile)
                {
                    if (file.Name == remoteFileName)
                    {
                        return true;
                    }
                }
                return false;
            }
            catch (Exception ep)
            {
                this._ErrorMsg = ep.ToString();
                throw ep;
            }
        }
        #endregion

        #region 目录切换操作
        /// <summary>
        /// 进入一个目录
        /// </summary>
        /// <param name="remoteDirectoryName">
        /// 新目录的名字。
        /// 说明：如果新目录是当前目录的子目录，则直接指定子目录。如: SubFolder1/SubFolder2 ；
        /// 如果新目录不是当前目录的子目录，则必须从根目录一级一级的指定。如：/NewFolder/SubFolder1/SubFolder2
        /// 如果目录格式为“/”或“/Folder”，则从根目录开始查找
        /// </param>
        public bool GotoDirectory(string remoteDirectoryName)
        {/*注：对remoteDirecName是this.FtpUrl.Path的子串的优化，不在考虑之中
            比如 remoteDirecName="/";
            this.FtpUrl.Path="/f1/f2/f3/f4/f5/f6";
            存在优化较复杂。
            */
            bool Success = false;
            remoteDirectoryName = remoteDirectoryName.Trim();
            try
            {
                remoteDirectoryName = remoteDirectoryName.Replace("\\", "/");
                if (remoteDirectoryName.Equals(this.FtpUrl.Path))
                {/*与当前工作文件夹相同则直接返回*/
                    Success = true;
                } else if (remoteDirectoryName.IndexOf(this.FtpUrl.Path) == 0)
                {/*this.FtpUrl.Path是remoteDirecName的子串,且是从0位置开始， 下面要做的，就是切出后面的字符串*/
                    int index = this.FtpUrl.Path.Length;
                    string dic_name = remoteDirectoryName.Substring(index, remoteDirectoryName.Length - index);
                    string[] DirectoryNames = dic_name.Split(new char[] { '/' });
                    foreach (string dir in DirectoryNames)
                    {
                        this._FtpConnect.ChangeFolder(dir);
                    }
                    Success = true;
                }
                else { /*其它条件*/
                    string[] DirectoryNames = remoteDirectoryName.Split(new char[] { '/' });
                    if (DirectoryNames[0] == "")/*表示以“/xx”开头*/
                    {
                        DirectoryNames[0] = "/";
                        this.FtpUrl.Path = "";
                    }
                    foreach (string dir in DirectoryNames)
                    {
                        this._FtpConnect.ChangeFolder(dir);
                    }
                    Success = true;
                }

            }
            catch (Exception ep)
            {
                this._ErrorMsg = ep.ToString();
                throw ep;
            }
            return Success;
        }
        /// <summary>
        /// 从当前工作目录往上一级目录
        /// </summary>
        public bool ComeoutDirectory()
        {
            char[] sp = new char[1] { '/' };

            string[] strDir = this.FtpUrl.Path.Split(sp, StringSplitOptions.None);
            if (strDir.Length == 1)
            {
                this._ErrorMsg = "当前目录已经是根目录！";
                throw new Exception(this._ErrorMsg);
            }
            else
            {
                this._FtpConnect.ChangeFolder("..");/*返回上一层*/
            }
            return true;

        }
        #endregion

        #region 文件、目录名称有效性判断
        /// <summary>
        /// 判断目录名中字符是否合法
        /// </summary>
        /// <param name="remoteDirectoryName">目录名称</param>
        private bool isValidPathChars(string remoteDirectoryName)
        {
            char[] invalidPathChars = Path.GetInvalidPathChars();
            char[] DirChar = remoteDirectoryName.ToCharArray();
            foreach (char C in DirChar)
            {
                if (Array.BinarySearch(invalidPathChars, C) >= 0)
                {
                    return false;
                }
            }
            return true;
        }
        /// <summary>
        /// 判断文件名中字符是否合法
        /// </summary>
        /// <param name="remoteFileName">文件名称</param>
        private bool isValidFileChars(string remoteFileName)
        {
            char[] invalidFileChars = Path.GetInvalidFileNameChars();
            char[] NameChar = remoteFileName.ToCharArray();
            foreach (char C in NameChar)
            {
                if (Array.BinarySearch(invalidFileChars, C) >= 0)
                {
                    return false;
                }
            }
            return true;
        }
        #endregion

        /// <summary>
        /// 从Windows格式中返回文件信息
        /// </summary>
        /// <param name="Record">文件信息</param>
        private FtpFileInfo parseFileStructFromWindowsStyleRecord(string Record)
        {
            FtpFileInfo f = new FtpFileInfo();
            string processstr = Record.Trim();
            string dateStr = processstr.Substring(0, 8);
            processstr = (processstr.Substring(8, processstr.Length - 8)).Trim();
            string timeStr = processstr.Substring(0, 7);
            processstr = (processstr.Substring(7, processstr.Length - 7)).Trim();
            DateTimeFormatInfo myDTFI = new CultureInfo("en-US", false).DateTimeFormat;
            myDTFI.ShortTimePattern = "t";
            f.CreateTime = DateTime.Parse(dateStr + " " + timeStr, myDTFI);
            if (processstr.Substring(0, 5) == "<DIR>")
            {
                f.IsFolder = true;
                processstr = (processstr.Substring(5, processstr.Length - 5)).Trim();
            }
            else
            {
                string[] strs = processstr.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);   // true);
                processstr = strs[1];
                //processstr = processstr.Substring(processstr.IndexOf(' ') + 1);
                f.FileSize = Convert.ToInt64(strs[0]);
                f.IsFolder = false;
            }
            f.Name = processstr;
            return f;
        }
        /// <summary>
        /// 根据文件列表记录猜想文件列表类型
        /// </summary>
        /// <param name="recordList"></param>
        /// <returns></returns>
        private FileListStyle guessFileListStyle(string[] recordList)
        {
            foreach (string s in recordList)
            {
                if (s.Length > 10 && Regex.IsMatch(s.Substring(0, 10), "(-|d)(-|r)(-|w)(-|x)(-|r)(-|w)(-|x)(-|r)(-|w)(-|x)"))
                {
                    return FileListStyle.UnixStyle;
                }
                else if (s.Length > 8 && Regex.IsMatch(s.Substring(0, 8), "[0-9][0-9]-[0-9][0-9]-[0-9][0-9]"))
                {
                    return FileListStyle.WindowsStyle;
                }
            }
            return FileListStyle.Unknown;
        }
        /// <summary>
        /// 从Unix格式中返回文件信息
        /// </summary>
        /// <param name="Record">文件信息</param>
        private FtpFileInfo parseFileStructFromUnixStyleRecord(string Record)
        {
            FtpFileInfo f = new FtpFileInfo();
            string processstr = Record.Trim();
            f.Flags = processstr.Substring(0, 10);
            f.IsFolder = (f.Flags[0] == 'd');
            processstr = (processstr.Substring(11)).Trim();
            cutSubstringFromStringWithTrim(ref processstr, ' ', 0);   //跳过一部分
            f.Owner = cutSubstringFromStringWithTrim(ref processstr, ' ', 0);
            f.Group = cutSubstringFromStringWithTrim(ref processstr, ' ', 0);
            f.FileSize = Convert.ToInt32(cutSubstringFromStringWithTrim(ref processstr, ' ', 0));
            //_cutSubstringFromStringWithTrim(ref processstr, ' ', 0);   //跳过一部分
            string yearOrTime = processstr.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)[2];
            if (yearOrTime.IndexOf(":") >= 0)  //time
            {
                processstr = processstr.Replace(yearOrTime, DateTime.Now.Year.ToString());
            }
            f.CreateTime = DateTime.Parse(cutSubstringFromStringWithTrim(ref processstr, ' ', 8));
            f.Name = processstr;   //最后就是名称
            return f;
        }
        /// <summary>
        /// 按照一定的规则进行字符串截取
        /// </summary>
        /// <param name="s">截取的字符串</param>
        /// <param name="c">查找的字符</param>
        /// <param name="startIndex">查找的位置</param>
        private string cutSubstringFromStringWithTrim(ref string s, char c, int startIndex)
        {
            int pos1 = s.IndexOf(c, startIndex);
            string retString = s.Substring(0, pos1);
            s = (s.Substring(pos1)).Trim();
            return retString;
        }
        #endregion

        private long readFileSize( FTPConnect ftpCon, FtpFileInfo ftpFileInfo)
        {/*ftpCon为null在这里不考虑*/
            try
            {
                ftpCon.TransformDir(ftpFileInfo.DirectoryName);/*切换目录*/
                ftpCon.SendCommand("SIZE " + ftpFileInfo.Name);
                long lFileSize = 0;
                if (ftpCon.ReplyCode == 213)
                    lFileSize = Int64.Parse(ftpCon.ReplyString.Substring(4));
                else
                    throw new IOException(ftpCon.ReplyString.Substring(4));
                return lFileSize;
            }
            catch (Exception ex)
            {
                string log = string.Format("Guid:{0} ftpConCurPath:{1} ftpFullPath:{2} reason:{3}", ftpCon.ID,ftpCon.FtpUrl.Path, ftpFileInfo.FullName, ex.Message);
                LogHelper.Error(typeof(FtpHelper), log);
                throw (ex);
            }
        }
        #endregion

        #region 上传文件
        /// <summary>
        /// 直接上传文件
        /// </summary>
        /// <param name="ftpFileName">上传的目标全路径与文件名</param>
        /// <param name="isContinueUpload">是否断点续传</param>
        /// <returns>上传是否成功</returns>
        public bool UploadFileSync(byte[] fileBytes, string ftpFileName, bool isContinueUpload)
        {
            bool result = true;
            
            Socket socketData = null;
            FtpFileInfo ffi = new FtpFileInfo(this.FtpUrl.Path + "/" + ftpFileName);
            int bytesRead;
            long lOffset = 0, lTotalReaded = 0;
            try
            {
                #region 得到服务器上的文件大小
                if (isContinueUpload)
                {
                    lOffset = this.readFileSize( this._FtpConnect, ffi );
                }
                #endregion
                #region 开始上传
                lTotalReaded = lOffset;
                socketData = this._FtpConnect.CreateDataSocket();
                if (lOffset > 0)
                    this._FtpConnect.SendCommand("APPE " + ftpFileName);
                else
                    this._FtpConnect.SendCommand("STOR " + ftpFileName);
                if (!(this._FtpConnect.ReplyCode == 125 || this._FtpConnect.ReplyCode == 150))
                    throw new IOException(this._FtpConnect.ReplyString.Substring(4));
                this._FtpConnect.DataTransmitting = true;
                while (true)
                {
                    if (!this._FtpConnect.DataTransmitting)
                    {
                        this.OnFileUploadCanceled(this._FtpConnect);
                        break;
                    }
                    this.OnFileUploading(this._FtpConnect, fileBytes.Length, lTotalReaded);
                    //开始上传资料
                    bytesRead = (int)((fileBytes.Length > lTotalReaded + this._FtpConnect.Buffer.Length) ? this._FtpConnect.Buffer.Length : (fileBytes.Length - lTotalReaded));
                    if (bytesRead == 0)
                        break;
                    Array.Copy(fileBytes, lTotalReaded, this._FtpConnect.Buffer, 0, bytesRead);
                    socketData.Send(this._FtpConnect.Buffer, bytesRead, 0);
                    lTotalReaded += bytesRead;
                }
                if (socketData.Connected)
                    socketData.Close();
                if (this._FtpConnect.DataTransmitting)
                {
                    if (!(this._FtpConnect.ReplyCode == 226 || this._FtpConnect.ReplyCode == 250))
                    {
                        this._FtpConnect.ReadReply();
                        if (!(this._FtpConnect.ReplyCode == 226 || this._FtpConnect.ReplyCode == 250))
                            throw new IOException(this._FtpConnect.ReplyString.Substring(4));
                    }
                    this.OnFileUploadCompleted(this._FtpConnect);
                }
                #endregion
            }
            catch (Exception ex)
            {
                result = false;
                throw (ex);
            }
            finally
            {
                if (socketData != null && socketData.Connected)
                    socketData.Close();
                this._FtpConnect.DisConnect();
                this.m_FTPConnectList.Remove(this._FtpConnect);
            }
            return result;
        }
        /// <summary>
        /// 直接上传文件
        /// </summary>
        /// <param name="filePath">上传文件的全路径</param>
        /// <param name="ftpFileName">上传的目标全路径与文件名</param>
        /// <param name="isContinueUpload">是否断点续传</param>
        /// <returns>上传是否成功</returns>
        public bool UploadFileSync(string filePath, string ftpFileName, bool isContinueUpload)
        {
            FtpFileInfo ffi = new FtpFileInfo(this.FtpUrl.Path + "/" + ftpFileName);
            return threadUploadFile(filePath, ffi, isContinueUpload, "");
        }
        /// <summary>
        /// 上传文件
        /// </summary>
        /// <param name="filePath"></param>
        /// <param name="ftpFileName"></param>
        public string UploadFileAsync(string filePath, string ftpFileName)
        {
            return this.UploadFileAsync(filePath, ftpFileName, false);
        }
        /// <summary>
        /// 上传文件
        /// </summary>
        /// <param name="filePath">上传文件的全路径</param>
        /// <param name="ftpFileName">上传的目标全路径 包含了用户名用户密码与文件名</param>
        /// <param name="isContinueUpload">是否断点续传</param>
        /// <returns>返回控制上传下载的ID</returns>
        public string UploadFileAsync(string filePath, string ftpFileName, bool isContinueUpload)
        {
            String strFTPId = System.Guid.NewGuid().ToString();
            IList<object> objList = new List<object> { filePath, new FtpFileInfo(this.FtpUrl.Path+"/"+ftpFileName), isContinueUpload, strFTPId };
            System.Threading.Thread threadUpload = new System.Threading.Thread(new System.Threading.ParameterizedThreadStart(threadUploadFile));
            threadUpload.Start(objList);  //开始采用线程方式下载
            return strFTPId;
        }
        /// <summary>
        /// 线程接收上传
        /// </summary>
        /// <param name="obj"></param>
        private void threadUploadFile(object obj)
        {
            string filePath;
            FtpFileInfo ftpFileInfo;
            bool isContinueUpload;
            string strFTPId;
            IList<object> objList = obj as IList<object>;
            if (objList != null && objList.Count == 4)
            {
                filePath = objList[0] as string;
                ftpFileInfo = objList[1] as FtpFileInfo;
                isContinueUpload = (bool)objList[2];
                strFTPId = objList[3] as string;
                this.threadUploadFile(filePath, ftpFileInfo, isContinueUpload, strFTPId);
            }
        }
        /// <summary>
        /// 线程上传文件
        /// </summary>
        /// <param name="filePath">上传文件的全路径</param>
        /// <param name="ftpFileInfo">上传的目标全路径与文件名</param>
        /// <param name="isContinueUpload">是否断点续传</param>
        /// <returns>上传是否成功</returns>
        private bool threadUploadFile(string filePath, FtpFileInfo ftpFileInfo, bool isContinueUpload, string strFTPId)
        {
            bool result = true;

            FTPConnect ftpConnect = null;
            Socket socketData = null;
            FileStream fileStream = null;
            int iCharIndex = 0;
            int bytesRead;
            long lOffset = 0, lTotalReaded = 0;
            string strDirUrl, strDirUrlTemp;
            try
            {
                ftpConnect = new FTPConnect(this.FtpUrl,strFTPId);
                ftpConnect.Connect();
                ftpConnect.ChangeFolder(ftpFileInfo.DirectoryName);
                this.m_FTPConnectList.Add(ftpConnect);//添加到控制器中
                #region 得到服务器上的文件大小
                if (isContinueUpload)
                {
                    lOffset = this.readFileSize( ftpConnect, ftpFileInfo );
                }
                #endregion
                #region 开始上传
                fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                lTotalReaded = lOffset;
                fileStream.Seek(lOffset, SeekOrigin.Begin);
                socketData = ftpConnect.CreateDataSocket();
                if (lOffset > 0)
                    ftpConnect.SendCommand("APPE " + ftpFileInfo.Name);
                else
                    ftpConnect.SendCommand("STOR " + ftpFileInfo.Name);
                if (!(ftpConnect.ReplyCode == 125 || ftpConnect.ReplyCode == 150))
                    throw new IOException(ftpConnect.ReplyString.Substring(4));
                ftpConnect.DataTransmitting = true;
                while (true)
                {
                    if (!ftpConnect.DataTransmitting)
                    {
                        this.OnFileUploadCanceled(ftpConnect);
                        break;
                    }
                    this.OnFileUploading(ftpConnect, fileStream.Length, lTotalReaded);
                    //开始上传资料
                    bytesRead = fileStream.Read(ftpConnect.Buffer, 0, ftpConnect.Buffer.Length);
                    if (bytesRead == 0)
                        break;
                    socketData.Send(ftpConnect.Buffer, bytesRead, 0);
                    lTotalReaded += bytesRead;
                }
                fileStream.Close();
                if (socketData.Connected)
                    socketData.Close();
                if (ftpConnect.DataTransmitting)
                {
                    if (!(ftpConnect.ReplyCode == 226 || ftpConnect.ReplyCode == 250))
                    {
                        ftpConnect.ReadReply();
                        if (!(ftpConnect.ReplyCode == 226 || ftpConnect.ReplyCode == 250))
                            throw new IOException(ftpConnect.ReplyString.Substring(4));
                    }
                    this.OnFileUploadCompleted(ftpConnect);
                }
                #endregion
            }
            catch (Exception ex)
            {
                result = false;
                this.OnFtpError(ftpConnect, ex);
            }
            finally
            {
                if (fileStream != null)
                    fileStream.Close();
                if (socketData != null && socketData.Connected)
                    socketData.Close();
                ftpConnect.DisConnect();
                this.m_FTPConnectList.Remove(ftpConnect);
            }
            return result;
        }
        /// <summary>
        /// 取消正在上传的文件
        /// </summary>
        /// <returns></returns>
        public void CancelUploadFile(FTPConnect ftpConnect)
        {
            if (ftpConnect != null)
                ftpConnect.DataTransmitting = false;
        }
        /// <summary>
        /// 取消正在上传的文件
        /// </summary>
        /// <param name="strID"></param>
        public void CancelUploadFile(string strID)
        {
            foreach (FTPConnect ftp in this.m_FTPConnectList)
            {
                if (ftp != null && ftp.ID == strID)
                {
                    ftp.DataTransmitting = false;
                    break;
                }
            }
        }
        #endregion
        #region 下载文件
        /// <summary>
        /// 直接下载文件
        /// </summary>
        /// <param name="ftpFileName">要下载文件的路径</param>
        /// <param name="fileBytes">存贮的内容</param>
        /// <returns>下载是否成功</returns>
        public bool DownloadFileSync(string ftpFileName, out byte[] fileBytes)
        {
            bool result = true;

            FTPConnect ftpConnect = null;
            Socket socketData = null;
            FtpFileInfo ffi = new FtpFileInfo(this.FtpUrl.Path + "/" + ftpFileName);
            fileBytes = new byte[] { };
            int bytesRead;
            long lTotalReaded = 0, lFileSize;
            try
            {
                ftpConnect = new FTPConnect(this.FtpUrl);
                ftpConnect.Connect();
                ftpConnect.ChangeFolder(this.FtpUrl.Path);
                this.m_FTPConnectList.Add(ftpConnect);//添加到控制器中
                lFileSize = this.readFileSize( ftpConnect, ffi);

                socketData = ftpConnect.CreateDataSocket();
                fileBytes = new byte[lFileSize];
                ftpConnect.SendCommand("RETR " + ftpFileName);
                if (!(ftpConnect.ReplyCode == 150 || ftpConnect.ReplyCode == 125 || ftpConnect.ReplyCode == 226 || ftpConnect.ReplyCode == 250))
                    throw new IOException(ftpConnect.ReplyString.Substring(4));
                #region 开始下载
                ftpConnect.DataTransmitting = true;
                while (true)
                {
                    if (!ftpConnect.DataTransmitting)    //判断取消是否取消了下载
                    {
                        this.OnFileDownloadCanceled(ftpConnect);
                        break;
                    }
                    this.OnFileDownloading(ftpConnect, lFileSize, lTotalReaded);
                    //开始将文件流写入本地
                    bytesRead = socketData.Receive(ftpConnect.Buffer, ftpConnect.Buffer.Length, 0);
                    if (bytesRead <= 0)
                        break;
                    Array.Copy(ftpConnect.Buffer, 0, fileBytes, lTotalReaded, bytesRead);
                    lTotalReaded += bytesRead;
                }
                if (socketData.Connected)
                    socketData.Close();
                if (ftpConnect.DataTransmitting)
                {
                    if (!(ftpConnect.ReplyCode == 226 || ftpConnect.ReplyCode == 250))
                    {
                        ftpConnect.ReadReply();
                        if (!(ftpConnect.ReplyCode == 226 || ftpConnect.ReplyCode == 250))
                            throw new IOException(ftpConnect.ReplyString.Substring(4));
                    }
                    this.OnFileDownloadCompleted(ftpConnect);
                }
                #endregion
            }
            catch (Exception ex)
            {
                result = false;
                throw (ex);
            }
            finally
            {
                if (socketData != null && socketData.Connected)
                    socketData.Close();
                if (ftpConnect != null)
                {
                    ftpConnect.DisConnect();
                    this.m_FTPConnectList.Remove(ftpConnect);
                }
            }
            return result;
        }
        /// <summary>
        /// 直接下载文件
        /// </summary>
        /// <param name="ftpName">要下载文件的路径</param>
        /// <param name="filePath">目标存在全路径</param>
        /// <param name="isContinueDownload">是否断点续传</param>
        /// <returns>下载是否成功</returns>
        public bool DownloadFileSync(string ftpName, string filePath, bool isContinueDownload)
        {
            return threadDownloadFile(new FtpFileInfo(this.FtpUrl.Path + "/" + ftpName), filePath, isContinueDownload, "");
        }
        /// <summary>
        /// 下载文件
        /// </summary>
        /// <param name="ftpName"></param>
        /// <param name="targetFile"></param>
        public string DownloadFileAsync(string ftpName, string targetFile)
        {
            return this.DownloadFileAsync(ftpName, targetFile, false);
        }
        /// <summary>
        /// 下载文件
        /// </summary>
        /// <param name="ftpName">要下载文件的名称</param>
        /// <param name="filePath">目标存在路径包含文件名</param>
        /// <param name="isContinueDownload">是否断点续传</param>
        /// <returns>返回下载控制ID</returns>
        public string DownloadFileAsync(string ftpName, string filePath, bool isContinueDownload)
        {
            String strFTPId = System.Guid.NewGuid().ToString();
            IList<object> objList = new List<object> { new FtpFileInfo(this.FtpUrl.Path+"/"+ftpName), filePath, isContinueDownload, strFTPId };
            System.Threading.Thread threadDownload = new System.Threading.Thread(new System.Threading.ParameterizedThreadStart(threadDownloadFile));
            threadDownload.Start(objList);  //开始采用线程方式下载
            return strFTPId;
        }
        /// <summary>
        /// 线程接收下载
        /// </summary>
        /// <param name="obj"></param>
        private void threadDownloadFile(object obj)
        {
            FtpFileInfo dwnFFI;
            string targetFile;
            bool isContinueDownload;
            string strFTPId;
            IList<object> objList = obj as IList<object>;
            if (objList != null && objList.Count == 4)
            {
                dwnFFI = objList[0] as FtpFileInfo;
                targetFile = objList[1] as string;
                isContinueDownload = (bool)objList[2];
                strFTPId = objList[3] as String;
                this.threadDownloadFile(dwnFFI, targetFile, isContinueDownload, strFTPId);
            }
        }
        /// <summary>
        /// 线程下载文件
        /// </summary>
        /// <param name="ftpFileInfo">要下载文件的路径</param>
        /// <param name="filePath">目标存在全路径</param>
        /// <param name="isContinueDownload">是否断点续传</param>
        /// <returns>下载是否成功</returns>
        private bool threadDownloadFile(FtpFileInfo ftpFileInfo, string filePath, bool isContinueDownload, string strFTPId)
        {
            bool result = true;
            
            FTPConnect ftpConnect = null;
            Socket socketData = null;
            FileStream fileStream = null;
            int bytesRead;
            long lTotalReaded = 0, lFileSize;
            try
            {
                ftpConnect = new FTPConnect(this.FtpUrl, strFTPId);
                ftpConnect.Connect();
                ftpConnect.TransformDir(ftpFileInfo.DirectoryName);
                //ftpConnect.ChangeFolder(ftpFileInfo.DirectoryName);
                this.m_FTPConnectList.Add(ftpConnect);//添加到控制器中
                lFileSize = this.readFileSize( ftpConnect, ftpFileInfo);

                socketData = ftpConnect.CreateDataSocket();
                //断点续传长度的偏移量
                if (System.IO.File.Exists(filePath) && isContinueDownload)
                {
                    System.IO.FileInfo fiInfo = new FileInfo(filePath);
                    lTotalReaded = fiInfo.Length;
                    ftpConnect.SendCommand("REST " + fiInfo.Length.ToString());
                    if (ftpConnect.ReplyCode != 350)
                        throw new IOException(ftpConnect.ReplyString.Substring(4));
                }
                ftpConnect.SendCommand("RETR " + ftpFileInfo.Name);
                if (!(ftpConnect.ReplyCode == 150 || ftpConnect.ReplyCode == 125 || ftpConnect.ReplyCode == 226 || ftpConnect.ReplyCode == 250))
                    throw new IOException(ftpConnect.ReplyString.Substring(4));
                #region 开始下载
                string strTargetPath = filePath;
                strTargetPath = strTargetPath.Substring(0, strTargetPath.LastIndexOf("\\"));
                if (!System.IO.Directory.Exists(strTargetPath)) //判断目标路径是否存在，如果不存在就创建
                    System.IO.Directory.CreateDirectory(strTargetPath);
                if (System.IO.File.Exists(filePath) && isContinueDownload)  //目标文件已经是全路径了 断点续传
                    fileStream = new System.IO.FileStream(filePath, System.IO.FileMode.Append, System.IO.FileAccess.Write);
                else
                    fileStream = new System.IO.FileStream(filePath, System.IO.FileMode.Create, System.IO.FileAccess.Write);
                ftpConnect.DataTransmitting = true;
                while (true)
                {
                    if (!ftpConnect.DataTransmitting)    //判断取消是否取消了下载
                    {
                        this.OnFileDownloadCanceled(ftpConnect);
                        break;
                    }
                    this.OnFileDownloading(ftpConnect, lFileSize, lTotalReaded);
                    //开始将文件流写入本地
                    bytesRead = socketData.Receive(ftpConnect.Buffer, ftpConnect.Buffer.Length, 0);
                    if (bytesRead <= 0)
                        break;
                    fileStream.Write(ftpConnect.Buffer, 0, bytesRead);
                    lTotalReaded += bytesRead;
                }
                fileStream.Close();
                if (socketData.Connected)
                    socketData.Close();
                if (ftpConnect.DataTransmitting)
                {
                    if (!(ftpConnect.ReplyCode == 226 || ftpConnect.ReplyCode == 250))
                    {
                        ftpConnect.ReadReply();
                        if (!(ftpConnect.ReplyCode == 226 || ftpConnect.ReplyCode == 250))
                            throw new IOException(ftpConnect.ReplyString.Substring(4));
                    }
                    this.OnFileDownloadCompleted(ftpConnect);
                }
                #endregion
            }
            catch (Exception ex)
            {
                result = false;
                this.OnFtpError(ftpConnect, ex);
            }
            finally
            {
                if (fileStream != null)
                    fileStream.Close();
                if (socketData != null && socketData.Connected)
                    socketData.Close();
                if (ftpConnect != null)
                {
                    ftpConnect.DisConnect();
                    this.m_FTPConnectList.Remove(ftpConnect);
                }
            }
            return result;
        }
        /// <summary>
        /// 取消正在下载的文件
        /// </summary>
        /// <returns></returns>
        public void CancelDownloadFile(FTPConnect ftpConnect)
        {
            if (ftpConnect != null)
                ftpConnect.DataTransmitting = false;
        }
        /// <summary>
        /// 取消正在下载的文件
        /// </summary>
        /// <param name="strID"></param>
        public void CancelDownloadFile(string strID)
        {
            foreach (FTPConnect ftp in this.m_FTPConnectList)
            {
                if (ftp != null && ftp.ID == strID)
                {
                    ftp.DataTransmitting = false;
                    break;
                }
            }
        }
        #endregion
        #region 根据指定的ID查找FTPConnect
        /// <summary>
        /// 根据指定的ID查找FTPConnect
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        public FTPConnect FindFTPConnectByID(string id)
        {
            foreach (FTPConnect ftpConnect in this.m_FTPConnectList)
            {
                if (ftpConnect != null && ftpConnect.ID == id)
                    return ftpConnect;
            }
            return null;
        }
        #endregion

        #region 传输类型
        public enum FTPType
        {
            None,
            Upload,
            Download
        }
        #endregion

        #region 声明事件
        /// <summary>
        /// 正在下载文件
        /// </summary>
        public event FTPSendEventHandler FileDownloading;
        private delegate void OnFileDownloadingDelegate(FTPConnect ftpConnect, long lTotalBytes, long lBytesTransfered);
        /// <summary>
        /// 正在下载文件封装模式
        /// </summary>
        private void OnFileDownloading(FTPConnect ftpConnect, long lTotalBytes, long lBytesTransfered)
        {
            if (this.FileDownloading != null)
            {
                if (lBytesTransfered > lTotalBytes)
                    lBytesTransfered = lTotalBytes;
                System.ComponentModel.ISynchronizeInvoke aSynch = this.FileDownloading.Target as System.ComponentModel.ISynchronizeInvoke;
                if (aSynch != null && aSynch.InvokeRequired)
                    aSynch.Invoke(new OnFileDownloadingDelegate(OnFileDownloading), new object[] { ftpConnect, lTotalBytes, lBytesTransfered });
                else
                    this.FileDownloading(ftpConnect, new FTPSendEventArgs(lTotalBytes, lBytesTransfered));
            }
        }
        /// <summary>
        /// 文件下载完成
        /// </summary>
        public event EventHandler FileDownloadCompleted;
        private delegate void OnFileDownloadCompletedDelegate(FTPConnect ftpConnect);
        /// <summary>
        /// 文件下载完成封装模式
        /// </summary>
        private void OnFileDownloadCompleted(FTPConnect ftpConnect)
        {
            if (this.FileDownloadCompleted != null)
            {
                System.ComponentModel.ISynchronizeInvoke aSynch = this.FileDownloadCompleted.Target as System.ComponentModel.ISynchronizeInvoke;
                if (aSynch != null && aSynch.InvokeRequired)
                    aSynch.Invoke(new OnFileDownloadCompletedDelegate(OnFileDownloadCompleted), new object[] { ftpConnect });
                else
                    this.FileDownloadCompleted(ftpConnect, new EventArgs());
            }
        }
        /// <summary>
        /// 取消正在下载的文件
        /// </summary>
        public event EventHandler FileDownloadCanceled;
        private delegate void OnFileDownloadCanceledDelegate(FTPConnect ftpConnect);
        /// <summary>
        /// 取消正在下载的文件封装模式
        /// </summary>
        private void OnFileDownloadCanceled(FTPConnect ftpConnect)
        {
            if (this.FileDownloadCanceled != null)
            {
                System.ComponentModel.ISynchronizeInvoke aSynch = this.FileDownloadCanceled.Target as System.ComponentModel.ISynchronizeInvoke;
                if (aSynch != null && aSynch.InvokeRequired)
                    aSynch.Invoke(new OnFileDownloadCanceledDelegate(OnFileDownloadCanceled), new object[] { ftpConnect });
                else
                    this.FileDownloadCanceled(ftpConnect, new EventArgs());
            }
        }
        /// <summary>
        /// 正在上传文件
        /// </summary>
        public event FTPSendEventHandler FileUploading;
        private delegate void OnFileUploadingDelegate(FTPConnect ftpConnect, long lTotalBytes, long lBytesTransfered);
        /// <summary>
        /// 正在下载事件封装模式
        /// </summary>
        /// <param name="lTotalBytes"></param>
        /// <param name="lBytesTransfered"></param>
        private void OnFileUploading(FTPConnect ftpConnect, long lTotalBytes, long lBytesTransfered)
        {
            if (this.FileUploading != null)
            {
                if (lBytesTransfered > lTotalBytes)
                    lBytesTransfered = lTotalBytes;
                System.ComponentModel.ISynchronizeInvoke aSynch = this.FileUploading.Target as System.ComponentModel.ISynchronizeInvoke;
                if (aSynch != null && aSynch.InvokeRequired)
                    aSynch.Invoke(new OnFileUploadingDelegate(OnFileUploading), new object[] { ftpConnect, lTotalBytes, lBytesTransfered });
                else
                    this.FileUploading(ftpConnect, new FTPSendEventArgs(lTotalBytes, lBytesTransfered));
            }
        }
        /// <summary>
        /// 文件上传完成
        /// </summary>
        public event EventHandler FileUploadCompleted;
        private delegate void OnFileUploadCompletedDelegate(FTPConnect ftpConnect);
        private void OnFileUploadCompleted(FTPConnect ftpConnect)
        {
            if (this.FileUploadCompleted != null)
            {
                System.ComponentModel.ISynchronizeInvoke aSynch = this.FileUploadCompleted.Target as System.ComponentModel.ISynchronizeInvoke;
                if (aSynch != null && aSynch.InvokeRequired)
                    aSynch.Invoke(new OnFileUploadCompletedDelegate(OnFileUploadCompleted), new object[] { ftpConnect });
                else
                    this.FileUploadCompleted(ftpConnect, new EventArgs());
            }
        }
        /// <summary>
        /// 取消了上传文件
        /// </summary>
        public event EventHandler FileUploadCanceled;
        private delegate void OnFileUploadCanceledDelegate(FTPConnect ftpConnect);
        private void OnFileUploadCanceled(FTPConnect ftpConnect)
        {
            if (this.FileUploadCanceled != null)
            {
                System.ComponentModel.ISynchronizeInvoke aSynch = this.FileUploadCanceled.Target as System.ComponentModel.ISynchronizeInvoke;
                if (aSynch != null && aSynch.InvokeRequired)
                    aSynch.Invoke(new OnFileUploadCanceledDelegate(OnFileUploadCanceled), new object[] { ftpConnect });
                else
                    this.FileUploadCanceled(ftpConnect, new EventArgs());
            }
        }
        /// <summary>
        /// 传输过程发生错误事件
        /// </summary>
        public event FTPErrorEventHandler FtpError;
        private delegate void OnFtpErrorDelegate(FTPConnect ftpConnect, Exception error);
        public void OnFtpError(FTPConnect ftpConnect, Exception error)
        {
            if (this.FtpError != null)
            {
                System.ComponentModel.ISynchronizeInvoke aSynch = this.FtpError.Target as System.ComponentModel.ISynchronizeInvoke;
                if (aSynch != null && aSynch.InvokeRequired)
                    aSynch.Invoke(new OnFtpErrorDelegate(OnFtpError), new object[] { ftpConnect, error });
                else
                    this.FtpError(ftpConnect, new FTPErrorEventArgs(error));
            }
        }
        #endregion
    }
    #region 文件传输进度控制事件
    /// <summary>
    /// 无
    /// </summary>
    /// <param name="sender">是FtpConnect类型</param>
    /// <param name="e"></param>
    public delegate void FTPSendEventHandler(object sender, FTPSendEventArgs e);
    public class FTPSendEventArgs : System.EventArgs
    {
        private long m_totalbytes;          // Total Bytes
        private long m_bytestransfered;

        public FTPSendEventArgs()
        {
            m_totalbytes = 0;
            m_bytestransfered = 0;
        }
        public FTPSendEventArgs(long lTotalBytes, long lBytesTransfered)
        {
            m_totalbytes = lTotalBytes;
            m_bytestransfered = lBytesTransfered;
        }
        /// <summary>
        /// 总字节数
        /// </summary>
        public long TotalBytes
        {
            get { return m_totalbytes; }
            set { m_totalbytes = value; }
        }
        /// <summary>
        /// 已传输字节数
        /// </summary>
        public long BytesTransfered
        {
            get { return m_bytestransfered; }
            set { m_bytestransfered = value; }
        }
    }
    public delegate void FTPErrorEventHandler(object sender, FTPErrorEventArgs e);
    public class FTPErrorEventArgs : System.EventArgs
    {
        private Exception m_Error = null;
        public FTPErrorEventArgs() { }
        public FTPErrorEventArgs(Exception error)
        {
            this.m_Error = error;
        }
        /// <summary>
        /// 错误消息
        /// </summary>
        public Exception Error
        {
            get { return this.m_Error; }
            set { this.m_Error = value; }
        }
    }
    #endregion
}