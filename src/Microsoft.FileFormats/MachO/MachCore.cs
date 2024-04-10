// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Microsoft.FileFormats.MachO
{
    public class MachCore
    {
        private readonly MachOFile _machO;
        private readonly ulong _dylinkerHintAddress;
        private readonly Lazy<ulong> _dylinkerAddress;
        private readonly Lazy<MachDyld> _dylinker;
        private readonly Lazy<MachLoadedImage[]> _loadedImages;

        public MachCore(IAddressSpace dataSource, ulong dylinkerHintAddress = 0)
        {
            _machO = new MachOFile(dataSource);
            _dylinkerHintAddress = dylinkerHintAddress;
            _dylinkerAddress = new Lazy<ulong>(FindDylinker);
            _dylinker = new Lazy<MachDyld>(() => new MachDyld(new MachOFile(VirtualAddressReader.DataSource, DylinkerAddress, true)));
            _loadedImages = new Lazy<MachLoadedImage[]>(ReadImages);
        }

        public Reader VirtualAddressReader { get { return _machO.VirtualAddressReader; } }
        public ulong DylinkerAddress { get { return _dylinkerAddress.Value; } }
        public MachDyld Dylinker { get { return _dylinker.Value; } }
        public IEnumerable<MachLoadedImage> LoadedImages { get { return _loadedImages.Value; } }

        public bool IsValid()
        {
            return _machO.IsValid() && _machO.Header.FileType == MachHeaderFileType.Core;
        }

        private ulong FindDylinker()
        {
            if (_dylinkerHintAddress != 0 && IsValidDylinkerAddress(_dylinkerHintAddress))
            {
                return _dylinkerHintAddress;
            }
            if (TryFindDylinker(firstPass: true, out ulong position))
            {
                return position;
            }
            if (TryFindDylinker(firstPass: false, out position))
            {
                return position;
            }
            throw new BadInputFormatException("No dylinker module found");
        }

        private bool TryFindDylinker(bool firstPass, out ulong position)
        {
            const uint skip = 0x1000;
            const uint firstPassAttemptCount = 8;
            foreach (MachSegment segment in _machO.Segments)
            {
                ulong start = 0;
                ulong end = segment.LoadCommand.FileSize;
                if (firstPass)
                {
                    end = skip * firstPassAttemptCount;
                }
                else
                {
                    start = skip * firstPassAttemptCount;
                }
                for (ulong offset = start; offset < end; offset += skip)
                {
                    ulong possibleDylinker = segment.LoadCommand.VMAddress + offset;
                    if (IsValidDylinkerAddress(possibleDylinker))
                    {
                        position = possibleDylinker;
                        return true;
                    }
                }
            }
            position = 0;
            return false;
        }

        private bool IsValidDylinkerAddress(ulong possibleDylinkerAddress)
        {
            MachOFile dylinker = new(VirtualAddressReader.DataSource, possibleDylinkerAddress, true);
            return dylinker.IsValid() && dylinker.Header.FileType == MachHeaderFileType.Dylinker;
        }

        private MachLoadedImage[] ReadImages()
        {
            return Dylinker.Images.Select(i => new MachLoadedImage(new MachOFile(VirtualAddressReader.DataSource, i.LoadAddress, true), i)).ToArray();
        }
    }

    public class MachLoadedImage
    {
        private readonly DyldLoadedImage _dyldLoadedImage;

        public MachLoadedImage(MachOFile image, DyldLoadedImage dyldLoadedImage)
        {
            Image = image;
            _dyldLoadedImage = dyldLoadedImage;
        }

        public MachOFile Image { get; private set; }
        public ulong LoadAddress { get { return _dyldLoadedImage.LoadAddress; } }
        public string Path { get { return _dyldLoadedImage.Path; } }
    }

    public class MachDyld
    {
        private readonly MachOFile _dyldImage;
        private readonly Lazy<ulong> _dyldAllImageInfosAddress;
        private readonly Lazy<DyldImageAllInfosV2> _dyldAllImageInfos;
        private readonly Lazy<DyldImageInfo[]> _imageInfos;
        private readonly Lazy<DyldLoadedImage[]> _images;

        public MachDyld(MachOFile dyldImage)
        {
            _dyldImage = dyldImage;
            _dyldAllImageInfosAddress = new Lazy<ulong>(FindAllImageInfosAddress);
            _dyldAllImageInfos = new Lazy<DyldImageAllInfosV2>(ReadAllImageInfos);
            _imageInfos = new Lazy<DyldImageInfo[]>(ReadImageInfos);
            _images = new Lazy<DyldLoadedImage[]>(ReadLoadedImages);
        }

        public ulong AllImageInfosAddress { get { return _dyldAllImageInfosAddress.Value; } }
        public DyldImageAllInfosV2 AllImageInfos { get { return _dyldAllImageInfos.Value; } }
        public IEnumerable<DyldImageInfo> ImageInfos { get { return _imageInfos.Value; } }
        public IEnumerable<DyldLoadedImage> Images { get { return _images.Value; } }

        private ulong FindAllImageInfosAddress()
        {
            if (!_dyldImage.Symtab.TryLookupSymbol("dyld_all_image_infos", out ulong offset))
            {
                throw new BadInputFormatException("Can not find dyld_all_image_infos");
            }
            return offset + _dyldImage.PreferredVMBaseAddress;
        }

        private DyldImageAllInfosV2 ReadAllImageInfos()
        {
            return _dyldImage.VirtualAddressReader.Read<DyldImageAllInfosV2>(AllImageInfosAddress);
        }

        private DyldImageInfo[] ReadImageInfos()
        {
            return _dyldImage.VirtualAddressReader.ReadArray<DyldImageInfo>(AllImageInfos.InfoArray, AllImageInfos.InfoArrayCount);
        }

        private DyldLoadedImage[] ReadLoadedImages()
        {
            return ImageInfos.Select(i => new DyldLoadedImage(_dyldImage.VirtualAddressReader.Read<string>(i.PathAddress), i)).ToArray();
        }
    }

    public class DyldLoadedImage
    {
        private readonly DyldImageInfo _imageInfo;

        public DyldLoadedImage(string path, DyldImageInfo imageInfo)
        {
            Path = path;
            _imageInfo = imageInfo;
        }

        public string Path;
        public ulong LoadAddress { get { return _imageInfo.Address; } }
    }
}
