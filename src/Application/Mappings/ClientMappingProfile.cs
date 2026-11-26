using AutoMapper;
using DispatchGuard.Application.DTOs.Client;
using DispatchGuard.Core.Entities;

namespace DispatchGuard.Application.Mappings;

public class ClientMappingProfile : Profile
{
    public ClientMappingProfile()
    {
        CreateMap<Client, ClientDto>()
            .ForMember(dest => dest.CreatedAt, opt => opt.MapFrom(src => src.Audit.CreatedAt))
            .ForMember(dest => dest.UpdatedAt, opt => opt.MapFrom(src => src.Audit.UpdatedAt));
    }
}