// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Reflection;

namespace RefLoader
{
    public class Loader
    {
        public static void Main()
        {
            Invoked invoked = new Invoked();

            // pull out the type
            Type invokedType = typeof(Invoked);

            // pull out the method info for ExceptionWithHandler()
            MethodInfo mi = getMethod(typeof(Invoked), "ExceptionWithHandler");

            if (mi == null)
            {
                Console.WriteLine("There was a problem... ExceptionWithHandler was not found!");
            }

            // invoke ExceptionWithHandler()
            string retval = (string)mi.Invoke(invoked, null);
            Console.WriteLine(retval);

            // pull out the method info for ExceptionNoHandler();
            MethodInfo mi2 = getMethod(typeof(Invoked), "ExceptionNoHandler");

            // invoke ExceptionNoHandler()
            retval = (string)mi2.Invoke(invoked, null);
            Console.WriteLine(retval);

            Console.WriteLine("Finished");
        }

        //Gets MethodInfo object from a Type
        public static MethodInfo getMethod(Type t, string method)
        {
            TypeInfo ti = t.GetTypeInfo();
            MethodInfo mi = null;

            foreach (var m in ti.DeclaredMethods)
            {
                Console.WriteLine("Current METHOD: " + m.Name);
                if (m.Name.Equals(method))
                {
                    //found method
                    mi = m;
                    break;
                }
            }

            return mi;
        }
    }

    public class Invoked
    {
        public string ExceptionNoHandler()
        {
            Console.WriteLine("Beginning of ExceptionNoHandler()");
            int i = 1;
            if (i == 1)
                throw new Exception("Exception from InvokedCode.Invoked.ExceptionNoHandler()");

            return "ERROR: Returned from ExceptionNoHandler()";
        }

        public string ExceptionWithHandler()
        {
            Console.WriteLine("Beginning of ExceptionWithHandler()");

            try
            {
                throw new FormatException();
            }
            catch (FormatException)
            {
                Console.WriteLine("Caught FormatException");
            }

            return "SUCCESS:  Returned from ExceptionWithHandler";
        }

    }
}