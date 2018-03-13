﻿using System;
using System.IO;
using System.Net.Mime;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace OneDas.Hdf.Explorer.Web
{
    public class HomeController : Controller
    {
        private HdfExplorerOptions _options;

        public HomeController(IOptions<HdfExplorerOptions> options)
        {
            _options = options.Value;
        }

        public IActionResult Index()
        {
            return this.View("~/Web/Views/Home/Index.cshtml");
        }

        public IActionResult Error()
        {
            return this.View();
        }

        public FileResult Download([Bind(Prefix = "file")] string fileName)
        {
            try
            {
                string basePath = Path.Combine(_options.SupportDirectoryPath, "EXPORT");
                string filePath = Path.GetFullPath(Path.Combine(basePath, fileName));

                if (filePath.StartsWith(basePath))
                {
                    if (System.IO.File.Exists(filePath))
                    {
                        return this.File(new FileStream(filePath, FileMode.Open, FileAccess.Read), MediaTypeNames.Application.Octet, Path.GetFileName(filePath));
                    }
                    else
                    {
                        throw new Exception($"The requested file '{ fileName }' could not be found.");
                    }
                }
                else
                {
                    throw new Exception($"The requested file '{ fileName }' is not within the downloads folder.");
                }

            }
            catch (Exception)
            {
                throw new Exception($"An unknown error occured.");
            }
        }
    }
}
