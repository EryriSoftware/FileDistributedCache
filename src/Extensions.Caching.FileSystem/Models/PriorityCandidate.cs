using System;
using System.Collections.Generic;
using System.Text;

namespace Eryri.Extensions.Caching.FileSystem.Models;

internal record struct PriorityCandidate(string Key, long Version);
