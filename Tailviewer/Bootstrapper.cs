﻿using System;
using System.IO;
using Metrolib;
using log4net;
using log4net.Appender;
using log4net.Core;
using log4net.Layout;
using log4net.Repository.Hierarchy;

namespace Tailviewer
{
	public class Bootstrapper
		: AbstractBootstrapper
	{
		[STAThread]
		public static int Main(string[] args)
		{
			SetupLoggers();
			return App.Start(args);
		}

		private static void SetupLoggers()
		{
			var hierarchy = (Hierarchy) LogManager.GetRepository();

			var patternLayout = new PatternLayout
				{
					ConversionPattern = "%date [%thread] %-5level %logger - %message%newline"
				};
			patternLayout.ActivateOptions();

			var fileAppender = new RollingFileAppender
				{
					AppendToFile = true,
					File =
						Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Tailviewer",
						             "Tailviewer.log"),
					Layout = patternLayout,
					MaxSizeRollBackups = 20,
					MaximumFileSize = "1GB",
					RollingStyle = RollingFileAppender.RollingMode.Size,
					StaticLogFileName = false
				};
			fileAppender.ActivateOptions();
			hierarchy.Root.AddAppender(fileAppender);

			hierarchy.Root.Level = Level.Info;
			hierarchy.Configured = true;
		}
	}
}