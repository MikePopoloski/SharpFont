using SlimFont;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Test {
    class Program {
        static void Main (string[] args) {
            using (var loader = new TrueTypeLoader("../../../Fonts/OpenSans-Regular.ttf")) {
                loader.LoadFace();
            }
        }
    }
}
