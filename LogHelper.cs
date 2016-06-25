using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Reflection;
using log4net;

namespace SysMisc
{
    public class LogHelper
    {
        /// <summary>
        /// 输出日志到Log4Net
        /// </summary>
        /// <param name="t"></param>
        /// <param name="ex"></param>
        #region
        public static void Error(Type t, Exception ex)
        {
            try
            {
                ILog log = LogManager.GetLogger("logerror");
                log.Error("Error", ex);
                if (null != LogUpdateEvent)
                    LogUpdateEvent(LogType.ERROR, t, ex.ToString());
            }
            catch
            {
            }
        }
        #endregion

        /// <summary>
        /// 输出日志到Log4Net
        /// </summary>
        /// <param name="t"></param>
        /// <param name="msg"></param>
        #region
        public static void Error(Type t, string msg)
        {
            try
            {
                ILog log = LogManager.GetLogger("logerror");
                log.Error(msg);
                if (null != LogUpdateEvent)
                    LogUpdateEvent(LogType.ERROR, t, msg);
            }
            catch
            {
            }
        }
        
        public static void Info( Type t, string msg)
        {
            try
            {
                ILog log = LogManager.GetLogger("loginfo");
                log.Info(msg);
                if (null != LogUpdateEvent)
                    LogUpdateEvent(LogType.INFO, t, msg);
            }
            catch
            {
            }
        }
        
        public static void Debug(Type t, string msg)
        {
            try
            {
                ILog log = LogManager.GetLogger("logdebug");
                log.Debug(msg);
                if (null != LogUpdateEvent)
                    LogUpdateEvent( LogType.DEBUG, t, msg);
            }
            catch
            {
            }
        }

        #endregion

        public delegate void LogUpdateDelegate( LogType typ,Type t, string log);
        public static event LogUpdateDelegate LogUpdateEvent;

        public enum LogType
        {
            DEBUG,
            INFO,
            WARN,
            ERROR,
            FATAL
        }
    }
}
