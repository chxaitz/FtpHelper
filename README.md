# FtpHelper
一个C#写的Ftp Client的类，通过对网上现有资源的整合，修改，个人感觉还是蛮好用的。

##四个类
> * FtpHelper类 主类
> * FtpHelper.FtpConnect类 连接类，每个类，维护一个到Ftp Server的连接，一个FtpHelper可以有多个FtpConnect
> * FtpHelper.FtpFileInfo类 Ftp文件信息类，里面存储了，Ftp文件的信息，可以说是Ftp版的FileInfo
> * FtpHelper.FtpUrl类 每个FtpConnect有一个Url，用来维护FtpConnect连接的地址

##优点
> * 支持多线程
> * 支持异步
> * 支持进度

##注意事项
> * 在FtpHelper中有Directory和Folder的概念，一个Folder只表示一层文件夹，而Directory则可以表示多层文件夹。
比如 Folder=目录1 Directory=/目录1/目录2

##示例代码

```C#
    this._ftphelper = new FtpHelper(new Uri("ftp://127.0.0.1:21"), "root", "password");
    /*添加事件*/
    this._ftphelper.FtpError += Ftphelper_FtpError;
    this._ftphelper.FileDownloadCompleted += Ftphelper_FileDownloadCompleted;
    this._ftphelper.FileDownloading += Ftphelper_FileDownloading;
    this._ftphelper.FileUploadCompleted += Ftphelper_FileUploadCompleted;
    this._ftphelper.FileUploading += Ftphelper_FileUploading;
    /*切换目录*/
    if (true == this._ftphelper.Connect())
        {
        this._ftphelper.GotoDirectory("/");/*切换到根目录*/
        if( false == this._ftphelper.FolderExist( “Folder1”))
        {
            this._ftphelper.MakeDirectory(“Folder1”);
        }
        this._ftphelper.GotoDirectory("Folder1");
        }
    /*异步上传与下载*/
    /*注意：每一个异步下载都会创建一个FtpHelper.FtpConnect，从而导致同时下载的文件越多，就会在Ftp Server端看到有多个user同时登陆下载，这对于Ftp Server端有同时登陆人数限制的时候，会导致下载失败，现有的代码，还没有做同时下载文件数量的限制和速度的限制，并且近期，可能也不打算更新这部分代码，因为我感觉够用了~O(∩_∩)O哈哈~*/
    string guid = this._ftphelper.DownloadFileAsync(ftp_name, local_path );
    string guid = this._ftphelper.UploadFileAsync(fi.FullName, fi.Name);
```
