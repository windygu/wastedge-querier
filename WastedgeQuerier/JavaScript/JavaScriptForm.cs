﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Jint.Parser;
using JintDebugger;
using WastedgeApi;

namespace WastedgeQuerier.JavaScript
{
    internal class JavaScriptForm : JintDebugger.JavaScriptForm
    {
        private readonly Api _api;

        public JavaScriptForm(Api api)
        {
            if (api == null)
                throw new ArgumentNullException(nameof(api));

            _api = api;

            Icon = NeutralResources.mainicon;
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);

            foreach (string fileName in Environment.GetCommandLineArgs().Skip(1).Where(p => p.EndsWith(".js") && File.Exists(p)))
            {
                OpenEditor(fileName);
            }
        }

        protected override void OnEngineCreated(EngineCreatedEventArgs e)
        {
            base.OnEngineCreated(e);

            e.Engine.SetValue("api", new Api(_api.Credentials));

            string resourceName = GetType().Namespace + ".Lib.js";

            using (var stream = GetType().Assembly.GetManifestResourceStream(resourceName))
            using (var reader = new StreamReader(stream))
            {
                e.Engine.Execute(reader.ReadToEnd(), new ParserOptions { Source = "Lib.js" });
            }
        }
    }
}
